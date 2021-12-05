using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion;

public class ExplosionGridData
{
    public int IterationOffset;

    public GridId GridId;
    public IMapGrid Grid;
    public TransformComponent? Transform;

    public Angle Angle;
    public Matrix3 Matrix;
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
    ///     Tiles that had an explosion propagate onto them from space.
    /// </summary>
    public HashSet<Vector2i> SpaceJump = new();

    public HashSet<Vector2i> TilesThatAreSpace = new();

    public Dictionary<Vector2i, AtmosDirection> EdgeTiles;

    public ExplosionGridData(GridId gridId, Dictionary<Vector2i, TileData> airtightMap,
        float maxIntensity, float intensityStepSize, string typeID, Dictionary<Vector2i, AtmosDirection> edgeTiles, TransformComponent? targetXform)
    {
        GridId = gridId;
        AirtightMap = airtightMap;
        MaxIntensity = maxIntensity;
        IntensityStepSize = intensityStepSize;
        TypeID = typeID;
        EdgeTiles = edgeTiles;

        Grid = IoCManager.Resolve<IMapManager>().GetGrid(gridId);

        foreach (var (tile, dir) in EdgeTiles)
        {
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (dir.HasFlag(direction))
                    TilesThatAreSpace.Add(tile.Offset(direction));
            }
        }

        // EXPLOSION TODO fix this jank. It can't be very performant
        foreach (var (tile, dir) in EdgeTiles)
        {
            foreach (var diagTile in ExplosionSystem.GetDiagonalNeighbors(tile))
            {
                if (TilesThatAreSpace.Contains(diagTile))
                    continue;

                if (!Grid.TryGetTileRef(diagTile, out var tileRef) || tileRef.Tile.IsEmpty)
                    TilesThatAreSpace.Add(diagTile);
            }
        }

        if (targetXform == null)
            return;

        Transform = IoCManager.Resolve<IEntityManager>().GetComponent<TransformComponent>(Grid.GridEntityId);

        var size = (float) Grid.TileSize;
        var offset = Matrix3.Identity;
        offset.R0C2 = size / 2;
        offset.R1C2 = size / 2;
        Angle = Transform.WorldRotation - targetXform.WorldRotation;
        Matrix = offset * Transform.WorldMatrix * targetXform.InvWorldMatrix;
        Offset = Angle.RotateVec((size / 4, size / 4));
    }

    public int AddNewTiles(int iteration, HashSet<Vector2i> spaceTiles)
    {
        HashSet<Vector2i> newTiles = new();
        var newTileCount = 0;

        // Use GetNewTiles to enumerate over neighbors of tiles that were recently added to tileSetList.
        foreach (var (newTile, direction) in GetNewTiles(iteration, spaceTiles))
        {
            var blockedDirections = AtmosDirection.Invalid;
            float sealIntegrity = 0;

            // is this a space tile?
            if (TilesThatAreSpace.Contains(newTile))
            {
                Processed.Add(newTile);

                if (Transform == null)
                    spaceTiles.Add(newTile);
                else
                {
                    var center = Matrix.Transform(newTile);
                    spaceTiles.Add(new((int) MathF.Floor(center.X + Offset.X), (int) MathF.Floor(center.Y + Offset.Y)));
                    spaceTiles.Add(new((int) MathF.Floor(center.X - Offset.Y), (int) MathF.Floor(center.Y + Offset.X)));
                    spaceTiles.Add(new((int) MathF.Floor(center.X - Offset.X), (int) MathF.Floor(center.Y - Offset.Y)));
                    spaceTiles.Add(new((int) MathF.Floor(center.X + Offset.Y), (int) MathF.Floor(center.Y - Offset.X)));
                }

                // space tiles don't contribute to newTileCount The space grid will do that.
                continue;
            }

            if (AirtightMap.TryGetValue(newTile, out var tileData))
            {
                blockedDirections = tileData.BlockedDirections;
                if (!tileData.ExplosionTolerance.TryGetValue(TypeID, out sealIntegrity))
                    sealIntegrity = float.MaxValue; // indestructible airtight entity
            }

            // If the explosion is entering this new tile from an unblocked direction, we add it directly
            if (blockedDirections == AtmosDirection.Invalid || // no blocked directions
                (direction == AtmosDirection.Invalid && (blockedDirections & EdgeTiles[newTile]) == 0) || // coming from space
                !blockedDirections.IsFlagSet(direction.GetOpposite())) // just unblocked
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

    // Get all of the new tiles that the explosion will cover in this new iteration.
    public IEnumerable<(Vector2i, AtmosDirection)> GetNewTiles(int iteration, HashSet<Vector2i> spaceTiles)
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
            enumerable = enumerable.Concat(GetNewDiagonalTiles(diagonal, spaceTiles));

        if (DelayedSpreaders.TryGetValue(iteration - 2, out var delayedAdjacent))
            enumerable = enumerable.Concat(GetNewAdjacentTiles(iteration, delayedAdjacent, true));

        if (DelayedSpreaders.TryGetValue(iteration - 3, out var delayedDiagonal))
            enumerable = enumerable.Concat(GetNewDiagonalTiles(delayedDiagonal, spaceTiles, true));

        enumerable = enumerable.Concat(GetDelayedNeighbors(iteration));
        
        return enumerable.Concat(IterateSpaceJump());
    }

    public IEnumerable<(Vector2i, AtmosDirection)> IterateSpaceJump()
    {
        foreach (var tile in SpaceJump)
        {
            if (!Processed.Contains(tile))
                yield return (tile, AtmosDirection.Invalid);
        }
        SpaceJump.Clear();
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
                        yield return (tile.Offset(direction), direction);
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
    IEnumerable<(Vector2i, AtmosDirection)> GetNewDiagonalTiles(IEnumerable<Vector2i> tiles, HashSet<Vector2i> spaceTiles, bool ignoreTileBlockers = false)
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



// This partial part of the explosion system has all of the functions used to create the actual explosion map.
// I.e, to get the sets of tiles & intensity values that describe an explosion.

public sealed partial class ExplosionSystem : EntitySystem
{
    /// <summary>
    ///     Upper limit on the explosion tile-fill iterations. Effectively limits the radius of an explosion.
    ///     Unless the explosion is very non-circular due to obstacles, <see cref="MaxArea"/> is likely to be reached before this
    /// </summary>
    public const int MaxRange = 100;

    /// <summary>
    ///     The maximum size an explosion can cover. Currently corresponds to a circle with ~50 tile radius.
    /// </summary>
    public const int MaxArea = (int) 3.14f * 2500;

    /// <summary>
    ///     This is the main explosion generating function. 
    /// </summary>
    /// <param name="gridId">The grid where the epicenter tile is located</param>
    /// <param name="initialTiles"> The initial set of tiles, the "epicenter" tiles.</param>
    /// <param name="typeID">The explosion type. this determines the explosion damage</param>
    /// <param name="totalIntensity">The final sum of the tile intensities. This governs the overall size of the
    /// explosion</param>
    /// <param name="slope">How quickly does the intensity decrease when moving away from the epicenter.</param>
    /// <param name="maxIntensity">The maximum intensity that the explosion can have at any given tile. This
    /// effectively caps the damage that this explosion can do.</param>
    /// <returns>A list of tile-sets and a list of intensity values which describe the explosion.</returns>
    public (List<float>, List<ExplosionGridData>) GetExplosionTiles(
        GridId gridId,
        HashSet<Vector2i> initialTiles,
        string typeID,
        float totalIntensity,
        float slope,
        float maxIntensity)
    {
        var intensityStepSize = slope / 2;

        if (!_airtightMap.TryGetValue(gridId, out var airtightMap))
            airtightMap = new();

        List<ExplosionGridData> gridData = new();

        if (totalIntensity <= 0 || slope <= 0)
            return (new(), gridData);

        ExplosionGridData initialGrid = new(gridId, airtightMap, maxIntensity, intensityStepSize, typeID, _gridEdges[gridId], null);
        gridData.Add(initialGrid);

        initialGrid.TileSets = new() { { 0, initialTiles } };

        // is this even a multi-tile explosion?
        if (totalIntensity < intensityStepSize * initialTiles.Count)
            return (new List<float> { totalIntensity / initialTiles.Count }, gridData);

        initialGrid.Processed = new(initialTiles);

        // these variables keep track of the total intensity we have distributed
        List<int> tilesInIteration = new() {initialTiles.Count};
        List<float> iterationIntensity = new() {intensityStepSize};
        var totalTiles = 0;
        var remainingIntensity = totalIntensity - intensityStepSize * initialTiles.Count;

        var iteration = 1;
        var maxIntensityIndex = 0;
        HashSet<Vector2i> spaceTiles = new();
        var intensityUnchangedLastLoop = false;

        // Main flood-fill loop
        while (remainingIntensity > 0 && iteration <= MaxRange)
        {
            // used to check if we can go home early
            var previousIntensity = remainingIntensity;

            // First, we  increase the intensity of previous iterations.
            for (var i = maxIntensityIndex; i < iteration; i++)
            {
                var intensityIncrease = MathF.Min(intensityStepSize, maxIntensity - iterationIntensity[i]);

                if (tilesInIteration[i] * intensityIncrease >= remainingIntensity)
                {
                    // there is not enough intensity left to distribute. add a fractional amount and break.
                    iterationIntensity[i] += (float) remainingIntensity / tilesInIteration[i];
                    remainingIntensity = 0;
                    break;
                }

                iterationIntensity[i] += intensityIncrease;
                remainingIntensity -= tilesInIteration[i] * intensityIncrease;

                // Has this tile-set has reached max intensity? If so, stop iterating over it in  future
                if (intensityIncrease < intensityStepSize)
                    maxIntensityIndex++;
            }

            if (remainingIntensity == 0) break;

            // Next, we will add a new iteration of tiles

            int newTileCount = 0;
            spaceTiles.Clear();
            foreach (var grid in gridData)
            {
                newTileCount += grid.AddNewTiles(iteration, spaceTiles);
            }

            // newTilesCount += AddSpaceTiles(spaceTiles)
            // make AddSpaceTiles return a list of grid tiles to jump to.
            // and then pass that INTO grid.AddNewTiles() at the next iteration.

            // Does adding these tiles bring us above the total target intensity?
            if (newTileCount * intensityStepSize >= remainingIntensity)
            {
                iterationIntensity.Add((float) remainingIntensity / newTileCount);
                break;
            }

            remainingIntensity -= newTileCount * intensityStepSize;
            iterationIntensity.Add(intensityStepSize);
            tilesInIteration.Add(newTileCount);

            totalTiles += newTileCount;
            if (totalTiles >= MaxArea)
                //Whooo! MAXCAP!
                break;

            // its possible the explosion has some max intensity and is stuck in a container whose walls it cannot break.
            // if the remaining intensity remains unchanged TWO loops in a row, we know that this is the case.
            if (intensityUnchangedLastLoop && remainingIntensity == previousIntensity)
            {
                break;
            }

            intensityUnchangedLastLoop = remainingIntensity == previousIntensity;
            iteration += 1;
        }

        foreach (var grid in gridData)
        {
            grid.CleanUp();
        }

        return (iterationIntensity, gridData);
    }
}

