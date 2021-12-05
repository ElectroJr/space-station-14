using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion;

// This partial part of the explosion system has all of the functions used to create the actual explosion map.
// I.e, to get the sets of tiles & intensity values that describe an explosion.

public class ExplosionGridData
{
    public int IterationOffset;

    public GridId GridId;

    public List<HashSet<Vector2i>> TileSets = new();

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
    public Dictionary<int, List<Vector2i>> DelayedSpreaders = new();

    // What iteration each delayed spreader originally belong to
    public Dictionary<Vector2i, int> DelayedSpreaderIteration = new();

    public List<float> IterationIntensity = new();

    // List of all tiles in the explosion.
    // Used to avoid explosions looping back in on themselves.
    public HashSet<Vector2i> Processed = new();

    public Dictionary<Vector2i, TileData> AirtightMap;

    public ExplosionGridData(GridId gridId, Dictionary<Vector2i, TileData> airtightMap)
    {
        GridId = gridId;
        AirtightMap = airtightMap;
    }
}

public sealed partial class ExplosionSystem : EntitySystem
{
    /// <summary>
    ///     The maximum size an explosion can cover. Currently corresponds to a circle with ~50 tile radius.
    /// </summary>
    public const int MaxArea = 7854;

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
    public ExplosionGridData GetExplosionTiles(
        GridId gridId,
        HashSet<Vector2i> initialTiles,
        string typeID,
        float totalIntensity,
        float slope,
        float maxIntensity)
    {
        if (!_airtightMap.TryGetValue(gridId, out var airtightMap))
        {
            airtightMap = new();
        }

        ExplosionGridData data = new(gridId, airtightMap);

        if (totalIntensity <= 0 || slope <= 0)
            return data;

        data.TileSets = new() { new(), initialTiles, new() };
        var intensityStepSize = slope / 2;
        var iteration = 3;

        // is this even a multi-tile explosion?
        if (totalIntensity < intensityStepSize * initialTiles.Count)
        {
            data.IterationIntensity = new() { 0, totalIntensity / initialTiles.Count, 0 };
            return data;
        }

        data.Processed = new(initialTiles);

        // these variables keep track of the total intensity we have distributed
        var tilesInIteration = new List<int> { 0, initialTiles.Count, 0 };
        int totalTiles = 0;
        data.IterationIntensity = new () { 0, intensityStepSize, 0 };
        float remainingIntensity = totalIntensity - intensityStepSize * initialTiles.Count;

        // keep track of tile iterations that have already reached maxIntensity
        int maxIntensityIndex = 1;

        bool exit = false;

        // Main flood-fill loop
        HashSet<Vector2i> newTiles;
        while (remainingIntensity > 0)
        {
            // used to check if we can go home early
            var previousIntensity = remainingIntensity;

            // First, we try to increase the intensity of previous iterations.
            for (var i = maxIntensityIndex; i < iteration; i++)
            {
                if (tilesInIteration[i] * intensityStepSize >= remainingIntensity &&
                    tilesInIteration[i] * (maxIntensity - data.IterationIntensity[i]) >= remainingIntensity)
                {
                    // there is not enough intensity left to distribute. add a fractional amount and break.
                    data.IterationIntensity[i] += (float) remainingIntensity / tilesInIteration[i];
                    exit = true;
                    break;
                }

                data.IterationIntensity[i] += intensityStepSize;
                remainingIntensity -= tilesInIteration[i] * intensityStepSize;

                if (data.IterationIntensity[i] >= maxIntensity)
                {
                    // reached max intensity, stop increasing intensity of this tile set and refund some intensity
                    remainingIntensity += tilesInIteration[i] * (data.IterationIntensity[i] - maxIntensity);
                    maxIntensityIndex = i + 1;
                    data.IterationIntensity[i] = maxIntensity;
                }
            }

            if (exit) break;

            // Next, we will add a new iteration of tiles
            newTiles = new();
            data.TileSets.Add(newTiles);
            tilesInIteration.Add(0);

            // We use the local GetNewTiles function to enumerate over neighbors of tiles that were recently added to tileSetList.
            foreach (var (newTile, direction) in GetNewTiles(data, iteration, typeID, intensityStepSize,  maxIntensity))
            {
                var blockedDirections = AtmosDirection.Invalid;
                float sealIntegrity = 0;

                if (airtightMap.TryGetValue(newTile, out var tileData))
                {
                    blockedDirections = tileData.BlockedDirections;
                    if (!tileData.ExplosionTolerance.TryGetValue(typeID, out sealIntegrity))
                        sealIntegrity = float.MaxValue; // indestructible airtight entity
                }

                // If the explosion is entering this new tile from an unblocked direction, we add it directly
                if (!blockedDirections.IsFlagSet(direction.GetOpposite()))
                {
                    data.Processed.Add(newTile);
                    newTiles.Add(newTile);

                    if (!data.DelayedSpreaderIteration.ContainsKey(newTile))
                    {
                        tilesInIteration[^1]++;
                    }

                    continue;
                }

                // the explosion is trying to enter from a blocked direction. Are we already attempting to enter
                // this tile from another blocked direction?
                if (data.DelayedSpreaderIteration.ContainsKey(newTile))
                    continue;

                // If this tile is blocked from all directions. then there is no way to snake around and spread
                // out from it without first breaking the blocker. So we can already mark it as processed for future iterations.
                if (blockedDirections == AtmosDirection.All)
                    data.Processed.Add(newTile);

                // At what explosion iteration would this blocker be destroyed?
                var clearIteration = iteration + (int) MathF.Ceiling(sealIntegrity / intensityStepSize);
                if (data.DelayedSpreaders.TryGetValue(clearIteration, out var list))
                    list.Add(newTile);
                else
                    data.DelayedSpreaders[clearIteration] = new() { newTile };

                data.DelayedSpreaderIteration[newTile] = iteration;
                tilesInIteration[^1]++;
            }

            // Does adding these tiles bring us above the total target intensity?
            if (tilesInIteration[^1] * intensityStepSize >= remainingIntensity)
            {
                data.IterationIntensity.Add((float) remainingIntensity / tilesInIteration[^1]);
                break;
            }
            data.IterationIntensity.Add(intensityStepSize);
            remainingIntensity -= tilesInIteration[^1] * intensityStepSize;

            totalTiles += tilesInIteration[^1];
            if (totalTiles >= MaxArea)
                //Whooo! MAXCAP!
                break;

            if (remainingIntensity == previousIntensity && !data.DelayedNeighbors.ContainsKey(iteration + 1))
                // this can only happen if all tiles are at maxTileIntensity and there were no neighbors to expand
                // to. Given that all tiles are at their maximum damage, no walls will be broken in future
                // iterations and we can just exit early.
                break;

            iteration += 1;
        }

        // final cleanup. Here we add delayedSpreaders to tileSetList.
        // TODO EXPLOSION don't do this? If an explosion was not able to "enter" a tile, just damage the blocking
        // entity, and not general entities on that tile.
        foreach (var (tile, index) in data.DelayedSpreaderIteration)
        {
            data.TileSets[index].Add(tile);
        }

        // Next, we remove duplicate tiles. Currently this can happen when a delayed spreader was circumvented.
        // E.g., a windoor blocked the explosion, but the explosion snaked around and added the tile before the
        // windoor broke.
        data.Processed.Clear();
        foreach (var tileSet in data.TileSets)
        {
            tileSet.ExceptWith(data.Processed);
            data.Processed.UnionWith(tileSet);
        }

        return data;
    }

    #region Local functions
    // Get all of the new tiles that the explosion will cover in this new iteration.
    IEnumerable<(Vector2i, AtmosDirection)> GetNewTiles(ExplosionGridData data, int iteration, string typeID,
        float intensityStepSize, float maxIntensity)
    {
        // firstly, if any delayed spreaders were cleared, add then to processed tiles to avoid unnecessary
        // calculations
        if (data.DelayedSpreaders.TryGetValue(iteration, out var clearedSpreaders))
            data.Processed.UnionWith(clearedSpreaders);

        // construct our enumerable from several other iterators
        var enumerable = GetNewAdjacentTiles(data, iteration, data.TileSets[iteration - 2], typeID, intensityStepSize, maxIntensity);
        enumerable = enumerable.Concat(GetNewDiagonalTiles(data, iteration, data.TileSets[iteration - 3]));
        enumerable = enumerable.Concat(GetDelayedTiles(data, iteration));

        // were there any delayed spreaders that we need to get the neighbors of?
        if (data.DelayedSpreaders.TryGetValue(iteration - 2, out var delayedAdjacent))
            enumerable = enumerable.Concat(GetNewAdjacentTiles(data, iteration, delayedAdjacent, typeID, intensityStepSize, maxIntensity, true));
        if (data.DelayedSpreaders.TryGetValue(iteration - 3, out var delayedDiagonal))
            enumerable = enumerable.Concat(GetNewDiagonalTiles(data, iteration, delayedDiagonal, true));

        return enumerable;
    }

    IEnumerable<(Vector2i, AtmosDirection)> GetDelayedTiles(ExplosionGridData data, int iteration)
    {
        if (!data.DelayedNeighbors.TryGetValue(iteration, out var delayed))
            yield break;

        foreach (var tile in delayed)
        {
            if (!data.Processed.Contains(tile.Item1))
                yield return tile;
        }

        data.DelayedNeighbors.Remove(iteration);
    }

    // Gets the tiles that are directly adjacent to other tiles. If a tile has an airtight entity that blocks
    // the explosion, those tiles are added to a list of delayed tiles that will be added to the explosion in
    // some future iteration.
    IEnumerable<(Vector2i, AtmosDirection)> GetNewAdjacentTiles(ExplosionGridData data,
        int iteration,
        IEnumerable<Vector2i> tiles,
        string typeID,
        float intensityStepSize,
        float maxIntensity,
        bool ignoreTileBlockers = false)
    {
        Vector2i newTile;
        foreach (var tile in tiles)
        {
            var blockedDirections = AtmosDirection.Invalid;
            float sealIntegrity = 0;

            // Note that if (grid, tile) is not a valid key, then airtight.BlockedDirections will default to 0 (no blocked directions)
            if (data.AirtightMap.TryGetValue(tile, out var tileData))
            {
                blockedDirections = tileData.BlockedDirections;
                if (!tileData.ExplosionTolerance.TryGetValue(typeID, out sealIntegrity))
                    sealIntegrity = float.MaxValue; // indestructible airtight entity
            }

            // First, yield any neighboring tiles that are not blocked by airtight entities on this tile
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (ignoreTileBlockers || !blockedDirections.IsFlagSet(direction))
                {
                    newTile = tile.Offset(direction);
                    if (!data.Processed.Contains(newTile))
                        yield return (tile.Offset(direction), direction);
                }
            }

            // If there are no blocked directions, we are done with this tile.
            if (ignoreTileBlockers || blockedDirections == AtmosDirection.Invalid)
                continue;

            // At what explosion iteration would this blocker be destroyed?
            var clearIteration = iteration + (int) MathF.Ceiling(sealIntegrity / intensityStepSize);

            // This tile has one or more airtight entities anchored to it blocking the explosion from traveling in
            // some directions. First, check whether this blocker can even be destroyed by this explosion?
            if (sealIntegrity > maxIntensity || float.IsNaN(sealIntegrity))
                continue;

            // We will add this neighbor to delayedTiles instead of yielding it directly during this iteration
            if (!data.DelayedNeighbors.TryGetValue(clearIteration, out var list))
            {
                list = new();
                data.DelayedNeighbors[clearIteration] = list;
            }

            // Check which directions are blocked and add them to the delayed tiles list
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (blockedDirections.IsFlagSet(direction))
                {
                    newTile = tile.Offset(direction);
                    if (!data.Processed.Contains(newTile))
                        list.Add((tile.Offset(direction), direction));
                }
            }
        }
    }

    // Get the tiles that are diagonally adjacent to some tiles. Note that if there are ANY air blockers in some
    // direction, that diagonal tiles is not added. The explosion will have to propagate along cardinal
    // directions.
    IEnumerable<(Vector2i, AtmosDirection)> GetNewDiagonalTiles(ExplosionGridData data, int iteration, IEnumerable<Vector2i> tiles, bool ignoreTileBlockers = false)
    {
        Vector2i newTile;
        AtmosDirection direction;
        foreach (var tile in tiles)
        {
            // Note that if a (grid,tile) is not a valid key, airtight.BlockedDirections defaults to 0 (no blocked directions).
            var airtight = data.AirtightMap.GetValueOrDefault(tile);
            var freeDirections = ignoreTileBlockers
                ? AtmosDirection.All
                : ~airtight.BlockedDirections;

            // Get the free directions of the directly adjacent tiles
            var freeDirectionsN = ~data.AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.North)).BlockedDirections;
            var freeDirectionsE = ~data.AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.East)).BlockedDirections;
            var freeDirectionsS = ~data.AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.South)).BlockedDirections;
            var freeDirectionsW = ~data.AirtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.West)).BlockedDirections;

            // North East
            if (freeDirections.IsFlagSet(AtmosDirection.NorthEast))
            {
                direction = AtmosDirection.Invalid;
                if (freeDirectionsN.IsFlagSet(AtmosDirection.SouthEast))
                    direction |= AtmosDirection.East;
                if (freeDirectionsE.IsFlagSet(AtmosDirection.NorthWest))
                    direction |= AtmosDirection.North;

                newTile = tile + (1, 1);
                if (direction != AtmosDirection.Invalid && !data.Processed.Contains(newTile))
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
                if (direction != AtmosDirection.Invalid && !data.Processed.Contains(newTile))
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
                if (direction != AtmosDirection.Invalid && !data.Processed.Contains(newTile))
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
                if (direction != AtmosDirection.Invalid && !data.Processed.Contains(newTile))
                    yield return (newTile, direction);
            }
        }
    }
    #endregion
}
