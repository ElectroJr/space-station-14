using System.Collections.Generic;
using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion.EntitySystems;

/// <summary>
///     This class exists to uhhh ensure there is sufficient code duplication... yeah...
/// </summary>
public class SpaceExplosion
{
    public Angle Angle = new();
    public Matrix3 Matrix = Matrix3.Identity;

    public Dictionary<int, HashSet<Vector2i>> TileSets = new();
    public HashSet<Vector2i> Processed = new();

    /// <summary>
    ///     The keys of this dictionary correspond to space tiles that intersect a grid. The values have information
    ///     about what grid (which could be more than one), and in what directions the space-based explosion is allowed
    ///     to propagate from this tile.
    /// </summary>
    public Dictionary<Vector2i, GridBlockData> GridBlockMap;

    public Dictionary<int, HashSet<Vector2i>> BlockedSpreader = new();

    public float IntensityStepSize;

    public SpaceExplosion(ExplosionSystem system, float tileSize, float intensityStepSize, MapId targetMap, GridId targetGridId)
    {
        // TODO EXPLOSION transform only in-range grids
        // TODO EXPLOSION for source-grid... don't transform, just add to map
        // TODO EXPLOSION for source-grid don't check unblocked directions... just block.
        // TODO EXPLOSION merge EdgeData and GridBlockMap

        GridBlockMap = system.TransformGridEdges(targetMap, targetGridId);
        system.GetUnblockedDirections(GridBlockMap, tileSize);

        IntensityStepSize = intensityStepSize;

        if (!targetGridId.IsValid())
            return;

        var targetGrid = IoCManager.Resolve<IMapManager>().GetGrid(targetGridId);
        var xform = IoCManager.Resolve<IEntityManager>().GetComponent<TransformComponent>(targetGrid.GridEntityId);
        Matrix = xform.WorldMatrix;
        Angle = xform.WorldRotation;
    }

    private AtmosDirection GetUnblocked(Vector2i tile)
    {
        return GridBlockMap.TryGetValue(tile, out var blocker) ? blocker.UnblockedDirections : AtmosDirection.All;
    }

    public int AddNewTiles(int iteration, HashSet<Vector2i> inputSpaceTiles, Dictionary<GridId, HashSet<Vector2i>> gridTileSets)
    {
        HashSet<Vector2i> newTiles = new();
        var newTileCount = 0;
        HashSet<Vector2i> blockedTiles = new();

        // Use GetNewTiles to enumerate over neighbors of tiles that were recently added to tileSetList.
        foreach (var newTile in GetNewTiles(iteration, inputSpaceTiles, blockedTiles))
        {
            newTiles.Add(newTile);
            newTileCount++;

            CheckGridTile(newTile, gridTileSets);
        }

        blockedTiles.ExceptWith(Processed);
        if (blockedTiles.Count != 0)
        {
            BlockedSpreader.Add(iteration, blockedTiles);
            foreach (var tile in blockedTiles)
            {
                newTileCount++;
                CheckGridTile(tile, gridTileSets);
            }
        }

        // might be empty, but we will fill this during Cleanup()
        if (newTileCount != 0)
            TileSets.Add(iteration, newTiles);

        return newTileCount;
    }

    private void CheckGridTile(Vector2i tile, Dictionary<GridId, HashSet<Vector2i>> gridTileSets)
    {
        // is this a grid tile?
        if (!GridBlockMap.TryGetValue(tile, out var blocker))
            return;

        foreach (var edge in blocker.BlockingGridEdges)
        {
            if (!edge.Grid.IsValid()) continue;

            if (!gridTileSets.TryGetValue(edge.Grid, out var set))
            {
                set = new();
                gridTileSets[edge.Grid] = set;
            }

            set.Add(edge.Tile);
        }
    }

    // Get all of the new tiles that the explosion will cover in this new iteration.
    public IEnumerable<Vector2i> GetNewTiles(int iteration, HashSet<Vector2i> inputSpaceTiles, HashSet<Vector2i> blockedTiles)
    {
        // construct our enumerable from several other iterators
        var enumerable = Enumerable.Empty<Vector2i>();

        if (TileSets.TryGetValue(iteration - 2, out var adjacent))
            enumerable = enumerable.Concat(GetNewAdjacentTiles(adjacent, blockedTiles));

        if (TileSets.TryGetValue(iteration - 3, out var diagonal))
            enumerable = enumerable.Concat(GetNewDiagonalTiles(diagonal, blockedTiles));
        
        return enumerable.Concat(IterateSpaceInterface(inputSpaceTiles));
    }

    public IEnumerable<Vector2i> IterateSpaceInterface(HashSet<Vector2i> inputSpaceTiles)
    {
        foreach (var tile in inputSpaceTiles)
        {
            if (Processed.Add(tile))
                yield return tile;
        }
    }

    IEnumerable<Vector2i> GetNewAdjacentTiles(IEnumerable<Vector2i> tiles, HashSet<Vector2i> blockedTiles)
    {
        Vector2i newTile;
        foreach (var tile in tiles)
        {
            var unblockedDirections = GetUnblocked(tile);

            // First, yield any neighboring tiles that are not blocked by airtight entities on this tile
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);

                if (!unblockedDirections.IsFlagSet(direction))
                    continue;

                newTile = tile.Offset(direction);
                
                if (GetUnblocked(newTile).IsFlagSet(direction.GetOpposite()))
                {
                    if (Processed.Add(newTile))
                        yield return newTile;
                }
                else
                {
                    blockedTiles.Add(newTile);
                }
            }
        }
    }

    IEnumerable<Vector2i> GetNewDiagonalTiles(IEnumerable<Vector2i> tiles, HashSet<Vector2i> blockedTiles)
    {
        Vector2i newTile;
        AtmosDirection direction;
        foreach (var tile in tiles)
        {
            // Note that if a (grid,tile) is not a valid key, airtight.BlockedDirections defaults to 0 (no blocked directions).
            var freeDirections = GetUnblocked(tile);

            // Get the free directions of the directly adjacent tiles            
            var freeDirectionsN = GetUnblocked(tile.Offset(AtmosDirection.North));
            var freeDirectionsE = GetUnblocked(tile.Offset(AtmosDirection.East));
            var freeDirectionsS = GetUnblocked(tile.Offset(AtmosDirection.South));
            var freeDirectionsW = GetUnblocked(tile.Offset(AtmosDirection.West));

            // North East
            if (freeDirections.IsFlagSet(AtmosDirection.NorthEast))
            {
                direction = AtmosDirection.Invalid;
                if (freeDirectionsN.IsFlagSet(AtmosDirection.SouthEast))
                    direction |= AtmosDirection.East;
                if (freeDirectionsE.IsFlagSet(AtmosDirection.NorthWest))
                    direction |= AtmosDirection.North;

                newTile = tile + (1, 1);

                if (direction != AtmosDirection.Invalid)
                {
                    if ( (direction & GetUnblocked(newTile)) != AtmosDirection.Invalid && Processed.Add(newTile))
                        yield return newTile;
                    else
                        blockedTiles.Add(newTile);
                }
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

                if (direction != AtmosDirection.Invalid)
                {
                    if ((direction & GetUnblocked(newTile)) != AtmosDirection.Invalid && Processed.Add(newTile))
                        yield return newTile;
                    else
                        blockedTiles.Add(newTile);
                }
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

                if (direction != AtmosDirection.Invalid)
                {
                    if ((direction & GetUnblocked(newTile)) != AtmosDirection.Invalid && Processed.Add(newTile))
                        yield return newTile;
                    else
                        blockedTiles.Add(newTile);
                }
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

                if (direction != AtmosDirection.Invalid)
                {
                    if ((direction & GetUnblocked(newTile)) != AtmosDirection.Invalid && Processed.Add(newTile))
                        yield return newTile;
                    else
                        blockedTiles.Add(newTile);
                }
            }
        }
    }

    internal void CleanUp()
    {
        // final cleanup. Here we add delayedSpreaders to tileSetList.
        // TODO EXPLOSION don't do this? If an explosion was not able to "enter" a tile, just damage the blocking
        // entity, and not general entities on that tile.
        foreach (var (index, set) in BlockedSpreader)
        {
            TileSets[index].UnionWith(set);
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
