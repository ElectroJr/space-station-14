using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion.EntitySystems;

// This partial part of the explosion system has all of the functions used to create the actual explosion map.
// I.e, to get the sets of tiles & intensity values that describe an explosion.

public sealed partial class ExplosionSystem : EntitySystem
{
    /// <summary>
    ///     This is the main explosion generating function. 
    /// </summary>
    /// <param name="epicenter">The centre of the explosion</param>
    /// <param name="typeID">The explosion type. this determines the explosion damage</param>
    /// <param name="totalIntensity">The final sum of the tile intensities. This governs the overall size of the
    /// explosion</param>
    /// <param name="slope">How quickly does the intensity decrease when moving away from the epicenter.</param>
    /// <param name="maxIntensity">The maximum intensity that the explosion can have at any given tile. This
    /// effectively caps the damage that this explosion can do.</param>
    /// <returns>A list of tile-sets and a list of intensity values which describe the explosion.</returns>
    public (List<float>, SpaceExplosion?, Dictionary<GridId, GridExplosion>, Matrix3)? GetExplosionTiles(
        MapCoordinates epicenter,
        string typeID,
        float totalIntensity,
        float slope,
        float maxIntensity)
    {

        if (totalIntensity <= 0 || slope <= 0)
            return null;

        Vector2i initialTile;
        GridId? epicentreGrid = null;
        var (localGrids, referenceGrid) = GetLocalGrids(epicenter, totalIntensity, slope, maxIntensity);

        // get the epicenter tile indices
        if (_mapManager.TryFindGridAt(epicenter, out var candidateGrid) &&
            candidateGrid.TryGetTileRef(candidateGrid.WorldToTile(epicenter.Position), out var tileRef) &&
            !tileRef.Tile.IsEmpty)
        {
            epicentreGrid = candidateGrid.Index;
            initialTile = tileRef.GridIndices;
        }
        else if (referenceGrid != null)
        {
            initialTile = _mapManager.GetGrid(referenceGrid.Value).WorldToTile(epicenter.Position);
        }
        else
        {
            initialTile = new Vector2i(
                    (int) Math.Floor(epicenter.Position.X / DefaultTileSize),
                    (int) Math.Floor(epicenter.Position.Y / DefaultTileSize));
        }

        // Main data for the exploding tiles in space and on various grids
        Dictionary<GridId, GridExplosion> gridData = new();
        SpaceExplosion? spaceData = null;

        // The intensity slope is how much the intensity drop over a one-tile distance. The actual algorithm step-size is half of thhat.
        var stepSize = slope / 2;

        // Hashsets used for when grid-based explosion propagate into space. Basically: used to move data between
        // `gridData` and `spaceData` in-between neighbor finding iterations.
        HashSet<Vector2i> gridToSpaceTiles = new();
        HashSet<Vector2i> previousGridToSpace;

        // As above, but for space-based explosion propagating from space onto grids.
        HashSet<GridId> encounteredGrids = new();
        Dictionary<GridId, HashSet<Vector2i>> spaceToGridTiles = new();
        Dictionary<GridId, HashSet<Vector2i>> previousSpaceToGrid;

        // variables for transforming between grid and space-coordiantes
        var spaceMatrix = Matrix3.Identity;
        var spaceAngle = Angle.Zero;
        if (referenceGrid != null)
        {
            var xform = Transform(_mapManager.GetGrid(referenceGrid.Value).GridEntityId);
            spaceMatrix = xform.WorldMatrix;
            spaceAngle = xform.WorldRotation;
        }

        // is the explosion starting on a grid?
        if (epicentreGrid != null)
        {
            // set up the initial `gridData` instance
            encounteredGrids.Add(epicentreGrid.Value);

            if (!_airtightMap.TryGetValue(epicentreGrid.Value, out var airtightMap))
                airtightMap = new();

            var initialGridData = new GridExplosion(
                epicentreGrid.Value,
                airtightMap,
                maxIntensity,
                stepSize,
                typeID,
                _gridEdges[epicentreGrid.Value],
                referenceGrid,
                spaceMatrix,
                spaceAngle);

            gridData[epicentreGrid.Value] = initialGridData;

            initialGridData.Processed.Add(initialTile);
            initialGridData.TileSets[0] = new() { initialTile };
        }
        else
        {
            // set up the space explosion data
            spaceData = new SpaceExplosion(this, stepSize, epicenter.MapId, referenceGrid, localGrids);
            spaceData.Processed.Add(initialTile);
            spaceData.TileSets[0] = new() { initialTile };

            // It might be the case that the initial space-explosion tile actually overlaps on a grid. In that case we
            // need to manually add it to the `spaceToGridTiles` dictionary. This would normally be done automatically
            // during the neighbor finding steps.
            if (spaceData.GridBlockMap.TryGetValue(initialTile, out var blocker))
            {
                foreach (var edge in blocker.BlockingGridEdges)
                {
                    if (edge.Grid == null) continue;

                    if (!spaceToGridTiles.TryGetValue(edge.Grid.Value, out var set))
                    {
                        set = new();
                        spaceToGridTiles[edge.Grid.Value] = set;
                    }

                    set.Add(edge.Tile);
                }
            }
        }

        // Is this even a multi-tile explosion?
        if (totalIntensity < stepSize)
            // Bit anticlimactic. All that set up for nothing....
            return (new List<float> { totalIntensity }, spaceData, gridData, spaceMatrix);
        
        // TThese variables keep track of the total intensity we have distributed
        List<int> tilesInIteration = new() { 1 };
        List<float> iterationIntensity = new() {stepSize};
        var totalTiles = 0;
        var remainingIntensity = totalIntensity - stepSize;

        var iteration = 1;
        var maxIntensityIndex = 0;

        // If an explosion is trapped in an indestructible room, we can end the neighbor finding steps early.
        // These variables are used to check if we can abort early.
        float previousIntensity;
        var intensityUnchangedLastLoop = false;

        // Main flood-fill / neighbor-finding loop
        while (remainingIntensity > 0 && iteration <= MaxRange && totalTiles < MaxArea)
        {
            previousIntensity = remainingIntensity;

            // First, we increase the intensity of the tiles that were already discovered in previous iterations.
            for (var i = maxIntensityIndex; i < iteration; i++)
            {
                var intensityIncrease = MathF.Min(stepSize, maxIntensity - iterationIntensity[i]);

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
                if (intensityIncrease < stepSize)
                    maxIntensityIndex++;
            }

            if (remainingIntensity <= 0) break;

            // Next, we will add a new iteration of tiles

            // In order to treat "cost" of moving off a grid on the same level as moving onto a grid, both space -> grid and grid -> space have to be delayed by one iteration.
            previousGridToSpace = gridToSpaceTiles;
            previousSpaceToGrid = spaceToGridTiles;
            gridToSpaceTiles = new();
            spaceToGridTiles = new();

            var newTileCount = 0;

            encounteredGrids.UnionWith(previousSpaceToGrid.Keys);
            foreach (var grid in encounteredGrids)
            {
                // is this a new grid, for which we must create a new explosion data set
                if (!gridData.TryGetValue(grid, out var data))
                {
                    if (!_airtightMap.TryGetValue(grid, out var airtightMap))
                        airtightMap = new();

                    data = new GridExplosion(
                        grid,
                        airtightMap,
                        maxIntensity,
                        stepSize,
                        typeID,
                        _gridEdges[grid],
                        referenceGrid,
                        spaceMatrix,
                        spaceAngle);

                    gridData[grid] = data;
                }

                // get the new neighbours, and populate gridToSpaceTiles in the process.
                newTileCount += data.AddNewTiles(iteration, previousSpaceToGrid.GetValueOrDefault(grid), gridToSpaceTiles);
            }

            // if space-data is null, but some grid-based explosion reached space, we need to initialize it.
            if (spaceData == null && previousGridToSpace.Count != 0)
                spaceData = new SpaceExplosion(this, stepSize, epicenter.MapId, referenceGrid, localGrids);

            // If the explosion has reached space, do that neighbors finding step as well.
            if (spaceData != null)
                newTileCount += spaceData.AddNewTiles(iteration, previousGridToSpace, spaceToGridTiles);

            // Does adding these tiles bring us above the total target intensity?
            tilesInIteration.Add(newTileCount);
            if (newTileCount * stepSize >= remainingIntensity)
            {
                iterationIntensity.Add((float) remainingIntensity / newTileCount);
                break;
            }

            // add the new tiles and decrement available intensity
            remainingIntensity -= newTileCount * stepSize;
            iterationIntensity.Add(stepSize);
            totalTiles += newTileCount;

            // It is possible that the explosion has some max intensity and is stuck in a container whose walls it
            // cannot break. if the remaining intensity remains unchanged TWO loops in a row, we know that this is the
            // case.
            if (intensityUnchangedLastLoop && remainingIntensity == previousIntensity)
            {
                break;
            }

            intensityUnchangedLastLoop = remainingIntensity == previousIntensity;
            iteration += 1;
        }

        // Neighbor finding is done. Perform final clean up and return.
        foreach (var grid in gridData.Values)
        {
            grid.CleanUp();
        }
        spaceData?.CleanUp();

        return (iterationIntensity, spaceData, gridData, spaceMatrix);
    }

    /// <summary>
    ///     Look for grids in an area and returns them. Also selects a special grid that will be used to determine the
    ///     orientation of an explosion in space.
    /// </summary>
    /// <remarks>
    ///     Note that even though an explosion may start ON a grid, the explosion in space may still be orientated to
    ///     match a separate grid. This is done so that if you have something like a tiny suicide-bomb shuttle exploding
    ///     near a large station, the explosion will still orient to match the station, not the tiny shuttle.
    /// </remarks>
    public (List<GridId>, GridId?) GetLocalGrids(MapCoordinates epicenter, float totalIntensity, float slope, float maxIntensity)
    {
        // get the approximate explosion radius. note that if the explosion is confined in some directions but not in
        // others, the actual explosion reach further than this distance from the epicenter.
        var radius = 0.5f + ApproxIntensityToRadius(totalIntensity, slope, maxIntensity);

        GridId? referenceGrid = null;
        float mass = 0;

        // First attempt to find a grid that is relatively close to the explosion's center. Instead of looking in a
        // diameter x diameter sized box, use a smaller box with radius-sized sides:
        var box = Box2.CenteredAround(epicenter.Position, (radius, radius));

        foreach (var grid in _mapManager.FindGridsIntersecting(epicenter.MapId, box))
        {
            if (TryComp(grid.GridEntityId, out PhysicsComponent? physics) && physics.Mass > mass)
            {
                mass = physics.Mass;
                referenceGrid = grid.Index;
            }
        }

        // Next, we use a much larger lookup to determine all grids relevant to the explosion. This is used to ignore
        // some grids during the grid-edge transformation steps. Basically: it means that if a grid is not in this set,
        // the explosion can never propagate from space onto this grid.

        // As mentioned before, the `diameter` is only indicative, as an explosion that is obstructed (e.g., in a
        // tunnel) may travel further away from the epicenter. But this is relatively rare, espc for space-traversing
        // explosions (a tunnel made out of other grids?), so instead of using the largest possible distance that an
        // explosion could travel and using that for the grid look up, we will just arbitrarily fudge the lookup size
        // to be twice the diameter.

        box = box.Scale(4); // box with width and height of 4*radius.
        var mapGrids = _mapManager.FindGridsIntersecting(epicenter.MapId, box).ToList();
        var grids = mapGrids.Select(x => x.Index).ToList();

        if (referenceGrid != null)
            return (grids, referenceGrid);

        // We still don't have are reference grid. So lets also look in the enlarged region
        foreach (var grid in mapGrids)
        {
            if (TryComp(grid.GridEntityId, out PhysicsComponent? physics) && physics.Mass > mass)
            {
                mass = physics.Mass;
                referenceGrid = grid.Index;
            }
        }

        return (grids, referenceGrid);
    }
}

