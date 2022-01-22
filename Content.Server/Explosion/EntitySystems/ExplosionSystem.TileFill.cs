using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion.EntitySystems;

// This partial part of the explosion system has all of the functions used to create the actual explosion map.
// I.e, to get the sets of tiles & intensity values that describe an explosion.

public sealed partial class ExplosionSystem : EntitySystem
{
    public const ushort DefaultTileSize = 1;

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
    /// <param name="map"></param>
    /// <param name="gridId">The grid where the epicenter tile is located</param>
    /// <param name="initialTile"> The initial "epicenter" tile.</param>
    /// <param name="referenceGrid">This grid (if any) determines the orientation of the explosion in space.</param>
    /// <param name="typeID">The explosion type. this determines the explosion damage</param>
    /// <param name="totalIntensity">The final sum of the tile intensities. This governs the overall size of the
    /// explosion</param>
    /// <param name="slope">How quickly does the intensity decrease when moving away from the epicenter.</param>
    /// <param name="maxIntensity">The maximum intensity that the explosion can have at any given tile. This
    /// effectively caps the damage that this explosion can do.</param>
    /// <returns>A list of tile-sets and a list of intensity values which describe the explosion.</returns>
    public (List<float>, SpaceExplosion?, Dictionary<GridId, GridExplosion>) GetExplosionTiles(
        MapId map,
        GridId gridId,
        Vector2i initialTile,
        GridId referenceGrid,
        string typeID,
        float totalIntensity,
        float slope,
        float maxIntensity)
    {
        if (totalIntensity <= 0 || slope <= 0)
            return (new(), null, new());

        Dictionary<GridId, GridExplosion> gridData = new();
        SpaceExplosion? spaceData = null;

        var intensityStepSize = slope / 2;

        HashSet<Vector2i> spaceTiles = new();
        HashSet<Vector2i> previousSpaceTiles;

        HashSet<GridId> knownGrids = new();
        Dictionary<GridId, HashSet<Vector2i>> gridTileSets = new();
        Dictionary<GridId, HashSet<Vector2i>> previousGridTileSets;

        // is the explosion starting on a grid
        if (gridId.IsValid())
        {
            if (!_airtightMap.TryGetValue(gridId, out var airtightMap))
                airtightMap = new();

            GridExplosion initialGrid = new(gridId, airtightMap, maxIntensity, intensityStepSize, typeID, _gridEdges[gridId]);
            knownGrids.Add(gridId);
            gridData.Add(gridId, initialGrid);
            initialGrid.Processed.Add(initialTile);
            initialGrid.TileSets[0] = new() { initialTile };
        }
        else
        {
            spaceData = new(this, DefaultTileSize, intensityStepSize, map, referenceGrid);
            spaceData.Processed.Add(initialTile);
            spaceData.TileSets[0] = new() { initialTile };

            // is this also a grid tile?
            if (spaceData.BlockMap.TryGetValue(initialTile, out var blocker))
            {
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
        }

        // is this even a multi-tile explosion?
        if (totalIntensity < intensityStepSize)
            return (new List<float> { totalIntensity }, spaceData, gridData);
        
        // these variables keep track of the total intensity we have distributed
        List<int> tilesInIteration = new() { 1 };
        List<float> iterationIntensity = new() {intensityStepSize};
        var totalTiles = 0;
        var remainingIntensity = totalIntensity - intensityStepSize;

        var iteration = 1;
        var maxIntensityIndex = 0;
        var intensityUnchangedLastLoop = false;

        // Main flood-fill loop
        while (remainingIntensity > 0 && iteration <= MaxRange && totalTiles < MaxArea)
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

            // In order to put the "iteration delay" of going off a grid on the same level as going ON a grid, both space-> grid and grid -> space have to be delayed by one iteration
            previousSpaceTiles = spaceTiles;
            spaceTiles = new();

            previousGridTileSets = gridTileSets;
            gridTileSets = new();

            int newTileCount = 0;

            knownGrids.UnionWith(previousGridTileSets.Keys);
            foreach (var grid in knownGrids)
            {
                if (!gridData.TryGetValue(grid, out var data))
                {
                    if (!_airtightMap.TryGetValue(grid, out var airtightMap))
                        airtightMap = new();

                    data = new(grid, airtightMap, maxIntensity, intensityStepSize, typeID, _gridEdges[grid]);
                    data.SetSpaceTransform(spaceData!);
                    gridData[grid] = data;
                }

                newTileCount += data.AddNewTiles(iteration, previousGridTileSets.GetValueOrDefault(grid), spaceTiles);
            }

            if (spaceData != null)
            {
                newTileCount += spaceData.AddNewTiles(iteration, previousSpaceTiles, gridTileSets);
            }
            else if (previousSpaceTiles.Count != 0)
            {
                spaceData = new(this, _mapManager.GetGrid(gridId).TileSize, intensityStepSize, map, referenceGrid);
                newTileCount += spaceData.AddNewTiles(iteration, previousSpaceTiles, gridTileSets);
            }

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

            // its possible the explosion has some max intensity and is stuck in a container whose walls it cannot break.
            // if the remaining intensity remains unchanged TWO loops in a row, we know that this is the case.
            if (intensityUnchangedLastLoop && remainingIntensity == previousIntensity)
            {
                break;
            }

            intensityUnchangedLastLoop = remainingIntensity == previousIntensity;
            iteration += 1;
        }

        foreach (var grid in gridData.Values)
        {
            grid.CleanUp();
        }
        spaceData?.CleanUp();

        return (iterationIntensity, spaceData, gridData);
    }
}

