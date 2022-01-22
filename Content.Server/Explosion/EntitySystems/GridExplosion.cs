using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion.EntitySystems;

public class GridExplosion
{
    public GridId GridId;
    public IMapGrid Grid;
    public bool NeedToTransform = false;

    public Angle Angle;
    public Matrix3 Matrix = Matrix3.Identity;
    public Vector2 Offset;

    public Dictionary<int, HashSet<Vector2i>> TileSets = new();

    // Tiles which neighbor an exploding tile, but have not yet had the explosion spread to them due to an
    // airtight entity on the exploding tile that prevents the explosion from spreading in that direction. These
    // will be added as a neighbor after some delay, once the explosion on that tile is sufficiently strong to
    // destroy the airtight entity.
    public Dictionary<int, List<(Vector2i, AtmosDirection)>> DelayedNeighbors = new();

    // This is a tile which is currently exploding, but has not yet started to spread the explosion to
    // surrounding tiles. This happens if the explosion attempted to enter this tile, and there was some
    // airtight entity on this tile blocking explosions from entering from that direction. Once the explosion is
    // strong enough to destroy this airtight entity, it will begin to spread the explosion to neighbors.
    // This maps an iteration index to a list of delayed spreaders that begin spreading at that iteration.
    public SortedDictionary<int, HashSet<Vector2i>> DelayedSpreaders = new();

    // What iteration each delayed spreader originally belong to
    public Dictionary<Vector2i, int> DelayedSpreaderIteration = new();

    // List of all tiles in the explosion.
    // Used to avoid explosions looping back in on themselves.
    public HashSet<Vector2i> Processed = new();

    public Dictionary<Vector2i, TileData> AirtightMap;

    public float MaxIntensity;
    public float IntensityStepSize;
    public string TypeID;

    /// <summary>
    ///     Tiles on this grid that are not actually on this grid.... uhh ... yeah.... look its faster than checking
    ///     atmos directions every iteration.
    /// </summary>
    public HashSet<Vector2i> SpaceTiles = new();

    public Dictionary<Vector2i, AtmosDirection> EdgeTiles;

    public GridExplosion(GridId gridId, Dictionary<Vector2i, TileData> airtightMap,
        float maxIntensity, float intensityStepSize, string typeID, Dictionary<Vector2i, AtmosDirection> edgeTiles)
    {
        GridId = gridId;
        AirtightMap = airtightMap;
        MaxIntensity = maxIntensity;
        IntensityStepSize = intensityStepSize;
        TypeID = typeID;
        EdgeTiles = edgeTiles;

        Grid = IoCManager.Resolve<IMapManager>().GetGrid(gridId);

        // initialise SpaceTiles
        foreach (var (tile, dir) in EdgeTiles)
        {
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (dir.IsFlagSet(direction))
                    SpaceTiles.Add(tile.Offset(direction));
            }
        }

        // we also need to include space tiles that are diagonally adjacent.
        // note that tiles diagonally adjacent to diagonal-edges are ALWAYS directly adjacent to a pure edge tile.

        // EXPLOSION TODO fix this jank. It can't be very performant
        foreach (var (tile, dir) in EdgeTiles)
        {
            foreach (var diagTile in ExplosionSystem.GetDiagonalNeighbors(tile))
            {
                if (SpaceTiles.Contains(diagTile))
                    continue;

                if (!Grid.TryGetTileRef(diagTile, out var tileRef) || tileRef.Tile.IsEmpty)
                    SpaceTiles.Add(diagTile);
            }
        }
    }

    public void SetSpaceTransform(SpaceExplosion space)
    {
        NeedToTransform = true;
        var transform = IoCManager.Resolve<IEntityManager>().GetComponent<TransformComponent>(Grid.GridEntityId);
        var size = (float) Grid.TileSize;
        Matrix.R0C2 = size / 2;
        Matrix.R1C2 = size / 2;
        Matrix *= transform.WorldMatrix * Matrix3.Invert(space.Matrix);
        Angle = transform.WorldRotation - space.Angle;
        Offset = Angle.RotateVec((size / 4, size / 4));
    }

    public int AddNewTiles(int iteration, HashSet<Vector2i>? inputGridTiles, HashSet<Vector2i> outputSpaceTiles)
    {
        HashSet<Vector2i> newTiles = new();
        var newTileCount = 0;

        // Use GetNewTiles to enumerate over neighbors of tiles that were recently added to tileSetList.
        foreach (var (newTile, direction) in GetNewTiles(iteration, inputGridTiles))
        {
            var blockedDirections = AtmosDirection.Invalid;
            float sealIntegrity = 0;

            // is this a space tile?
            if (ProcessSpace(newTile, outputSpaceTiles))
                continue;

            if (AirtightMap.TryGetValue(newTile, out var tileData))
            {
                blockedDirections = tileData.BlockedDirections;
                if (!tileData.ExplosionTolerance.TryGetValue(TypeID, out sealIntegrity))
                    sealIntegrity = float.MaxValue; // indestructible airtight entity
            }

            // If the explosion is entering this new tile from an unblocked direction, we add it directly
            if (blockedDirections == AtmosDirection.Invalid || // no blocked directions
               direction == AtmosDirection.Invalid && (blockedDirections & EdgeTiles[newTile]) == 0 || // coming from space
               direction != AtmosDirection.Invalid && !blockedDirections.IsFlagSet(direction.GetOpposite()) ) // just unblocked
            {
                Processed.Add(newTile);
                newTiles.Add(newTile);

                if (!DelayedSpreaderIteration.ContainsKey(newTile))
                {
                    newTileCount++;
                }

                continue;
            }

            // the explosion is trying to enter from a blocked direction. Are we already attempting to enter
            // this tile from another blocked direction?
            if (DelayedSpreaderIteration.ContainsKey(newTile))
                continue;

            // If this tile is blocked from all directions. then there is no way to snake around and spread
            // out from it without first breaking the blocker. So we can already mark it as processed for future iterations.
            if (blockedDirections == AtmosDirection.All)
                Processed.Add(newTile);

            // At what explosion iteration would this blocker be destroyed?
            var clearIteration = iteration + (int) MathF.Ceiling(sealIntegrity / IntensityStepSize);
            if (DelayedSpreaders.TryGetValue(clearIteration, out var list))
                list.Add(newTile);
            else
                DelayedSpreaders[clearIteration] = new() { newTile };

            DelayedSpreaderIteration[newTile] = iteration;
            newTileCount++;
        }

        // might be empty, but we will fill this during Cleanup()
        if (newTileCount != 0)
            TileSets.Add(iteration, newTiles);

        return newTileCount;
    }

    private bool ProcessSpace(Vector2i newTile, HashSet<Vector2i> outputSpaceTiles)
    {
        // is this a space tile?
        if (!SpaceTiles.Contains(newTile))
            return false;

        Processed.Add(newTile);

        if (!NeedToTransform)
            outputSpaceTiles.Add(newTile);
        else
        {
            var center = Matrix.Transform(newTile);
            outputSpaceTiles.Add(new((int) MathF.Floor(center.X + Offset.X), (int) MathF.Floor(center.Y + Offset.Y)));
            outputSpaceTiles.Add(new((int) MathF.Floor(center.X - Offset.Y), (int) MathF.Floor(center.Y + Offset.X)));
            outputSpaceTiles.Add(new((int) MathF.Floor(center.X - Offset.X), (int) MathF.Floor(center.Y - Offset.Y)));
            outputSpaceTiles.Add(new((int) MathF.Floor(center.X + Offset.Y), (int) MathF.Floor(center.Y - Offset.X)));
        }

        return true;
    }

    // Get all of the new tiles that the explosion will cover in this new iteration.
    public IEnumerable<(Vector2i, AtmosDirection)> GetNewTiles(int iteration, HashSet<Vector2i>? inputGridTiles)
    {
        // firstly, if any delayed spreaders were cleared, add them to processed tiles to avoid unnecessary
        // calculations
        if (DelayedSpreaders.TryGetValue(iteration, out var clearedSpreaders))
            Processed.UnionWith(clearedSpreaders);

        // construct our enumerable from several other iterators
        var enumerable = Enumerable.Empty<(Vector2i, AtmosDirection)>();

        if (TileSets.TryGetValue(iteration - 2, out var adjacent))
            enumerable = enumerable.Concat(GetNewAdjacentTiles(iteration, adjacent));

        if (TileSets.TryGetValue(iteration - 3, out var diagonal))
            enumerable = enumerable.Concat(GetNewDiagonalTiles(diagonal));

        if (DelayedSpreaders.TryGetValue(iteration - 2, out var delayedAdjacent))
            enumerable = enumerable.Concat(GetNewAdjacentTiles(iteration, delayedAdjacent, true));

        if (DelayedSpreaders.TryGetValue(iteration - 3, out var delayedDiagonal))
            enumerable = enumerable.Concat(GetNewDiagonalTiles(delayedDiagonal, true));

        enumerable = enumerable.Concat(GetDelayedNeighbors(iteration));

        return (inputGridTiles == null) ? enumerable : enumerable.Concat(IterateSpaceInterface(inputGridTiles));
    }

    public IEnumerable<(Vector2i, AtmosDirection)> IterateSpaceInterface(HashSet<Vector2i> inputGridTiles)
    {
        foreach (var tile in inputGridTiles)
        {
            if (!Processed.Contains(tile))
                yield return (tile, AtmosDirection.Invalid);
        }
    }

    IEnumerable<(Vector2i, AtmosDirection)> GetDelayedNeighbors(int iteration)
    {
        if (!DelayedNeighbors.TryGetValue(iteration, out var delayed))
            yield break;

        foreach (var tile in delayed)
        {
            if (!Processed.Contains(tile.Item1))
                yield return tile;
        }

        DelayedNeighbors.Remove(iteration);
    }

    // Gets the tiles that are directly adjacent to other tiles. If a currently exploding tile has an airtight entity
    // that blocks the explosion from propagating in some direction, those tiles are added to a list of delayed tiles
    // that will be added to the explosion in some future iteration.
    IEnumerable<(Vector2i, AtmosDirection)> GetNewAdjacentTiles(
        int iteration,
        IEnumerable<Vector2i> tiles,
        bool ignoreTileBlockers = false)
    {
        Vector2i newTile;
        foreach (var tile in tiles)
        {
            var blockedDirections = AtmosDirection.Invalid;
            float sealIntegrity = 0;

            // Note that if (grid, tile) is not a valid key, then airtight.BlockedDirections will default to 0 (no blocked directions)
            if (AirtightMap.TryGetValue(tile, out var tileData))
            {
                blockedDirections = tileData.BlockedDirections;
                if (!tileData.ExplosionTolerance.TryGetValue(TypeID, out sealIntegrity))
                    sealIntegrity = float.MaxValue; // indestructible airtight entity
            }

            // First, yield any neighboring tiles that are not blocked by airtight entities on this tile
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (ignoreTileBlockers || !blockedDirections.IsFlagSet(direction))
                {
                    newTile = tile.Offset(direction);
                    if (!Processed.Contains(newTile))
                        yield return (newTile, direction);
                }
            }

            // If there are no blocked directions, we are done with this tile.
            if (ignoreTileBlockers || blockedDirections == AtmosDirection.Invalid)
                continue;

            // At what explosion iteration would this blocker be destroyed?
            var clearIteration = iteration + (int) MathF.Ceiling(sealIntegrity / IntensityStepSize);

            // This tile has one or more airtight entities anchored to it blocking the explosion from traveling in
            // some directions. First, check whether this blocker can even be destroyed by this explosion?
            if (sealIntegrity > MaxIntensity || float.IsNaN(sealIntegrity))
                continue;

            // We will add this neighbor to delayedTiles instead of yielding it directly during this iteration
            if (!DelayedNeighbors.TryGetValue(clearIteration, out var list))
            {
                list = new();
                DelayedNeighbors[clearIteration] = list;
            }

            // Check which directions are blocked and add them to the delayed tiles list
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (blockedDirections.IsFlagSet(direction))
                {
                    newTile = tile.Offset(direction);
                    if (!Processed.Contains(newTile))
                        list.Add((tile.Offset(direction), direction));
                }
            }
        }
    }

    // Get the tiles that are diagonally adjacent to some tiles. Note that if there are ANY air blockers in some
    // direction, that diagonal tiles is not added. The explosion will have to propagate along cardinal
    // directions.
    IEnumerable<(Vector2i, AtmosDirection)> GetNewDiagonalTiles(IEnumerable<Vector2i> tiles, bool ignoreTileBlockers = false)
    {
        Vector2i newTile;
        AtmosDirection direction;
        foreach (var tile in tiles)
        {
            // Note that if a (grid,tile) is not a valid key, airtight.BlockedDirections defaults to 0 (no blocked directions).
            var airtight = AirtightMap.GetValueOrDefault(tile);
            var freeDirections = ignoreTileBlockers
                ? AtmosDirection.All
                : ~airtight.BlockedDirections;

            // Get the free directions of the directly adjacent tiles
            var freeDirectionsN = ~AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.North)).BlockedDirections;
            var freeDirectionsE = ~AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.East)).BlockedDirections;
            var freeDirectionsS = ~AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.South)).BlockedDirections;
            var freeDirectionsW = ~AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.West)).BlockedDirections;

            // North East
            if (freeDirections.IsFlagSet(AtmosDirection.NorthEast))
            {
                direction = AtmosDirection.Invalid;
                if (freeDirectionsN.IsFlagSet(AtmosDirection.SouthEast))
                    direction |= AtmosDirection.East;
                if (freeDirectionsE.IsFlagSet(AtmosDirection.NorthWest))
                    direction |= AtmosDirection.North;

                newTile = tile + (1, 1);
                if (direction != AtmosDirection.Invalid && !Processed.Contains(newTile))
                    yield return (newTile, direction);
            }

            // North West
            if (freeDirections.IsFlagSet(AtmosDirection.NorthWest))
            {
                direction = AtmosDirection.Invalid;
                if (freeDirectionsN.IsFlagSet(AtmosDirection.SouthWest))
                    direction |= AtmosDirection.West;
                if (freeDirectionsW.IsFlagSet(AtmosDirection.NorthEast))
                    direction |= AtmosDirection.North;

                newTile = tile + (-1, 1);
                if (direction != AtmosDirection.Invalid && !Processed.Contains(newTile))
                    yield return (newTile, direction);
            }

            // South East
            if (freeDirections.IsFlagSet(AtmosDirection.SouthEast))
            {
                direction = AtmosDirection.Invalid;
                if (freeDirectionsS.IsFlagSet(AtmosDirection.NorthEast))
                    direction |= AtmosDirection.East;
                if (freeDirectionsE.IsFlagSet(AtmosDirection.SouthWest))
                    direction |= AtmosDirection.South;

                newTile = tile + (1, -1);
                if (direction != AtmosDirection.Invalid && !Processed.Contains(newTile))
                    yield return (newTile, direction);
            }

            // South West
            if (freeDirections.IsFlagSet(AtmosDirection.SouthWest))
            {
                direction = AtmosDirection.Invalid;
                if (freeDirectionsS.IsFlagSet(AtmosDirection.NorthWest))
                    direction |= AtmosDirection.West;
                if (freeDirectionsW.IsFlagSet(AtmosDirection.SouthEast))
                    direction |= AtmosDirection.South;

                newTile = tile + (-1, -1);
                if (direction != AtmosDirection.Invalid && !Processed.Contains(newTile))
                    yield return (newTile, direction);
            }
        }
    }

    internal void CleanUp()
    {
        // final cleanup. Here we add delayedSpreaders to tileSetList.
        // TODO EXPLOSION don't do this? If an explosion was not able to "enter" a tile, just damage the blocking
        // entity, and not general entities on that tile.
        foreach (var (tile, index) in DelayedSpreaderIteration)
        {
            TileSets[index].Add(tile);
        }

        // Next, we remove duplicate tiles. Currently this can happen when a delayed spreader was circumvented.
        // E.g., a win-door blocked the explosion, but the explosion snaked around and added the tile before the
        // win-door broke.
        Processed.Clear();
        foreach (var tileSet in TileSets.Values)
        {
            tileSet.ExceptWith(Processed);
            Processed.UnionWith(tileSet);
        }
    }
}
