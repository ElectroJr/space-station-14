using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    // This partial part of the explosion system has all of the functions used to create the actual explosion map.
    // I.e, to get the sets of tiles & damage values that describe an explosion.

    public sealed partial class ExplosionSystem : EntitySystem
    {
        /// <summary>
        ///     The maximum size an explosion can cover. Currently corresponds to a circle with ~50 tile radius.
        /// </summary>
        public const int MaxArea = 7854;

        /// <summary>
        ///     Get the list of tiles that will be damaged when the given explosion is spawned.
        /// </summary>
        public (List<HashSet<Vector2i>>?, List<float>?) GetExplosionTiles(MapCoordinates epicenter, float totalIntensity, float slope, float maxIntensity)
        {
            if (totalIntensity <= 0)
                return (null, null);

            if (!_mapManager.TryFindGridAt(epicenter, out var grid))
                return (null, null);

            var epicenterTile = grid.TileIndicesFor(epicenter);

            return GetExplosionTiles(grid.Index, epicenterTile, totalIntensity, slope, maxIntensity);
        }

        /// <summary>
        ///     This function returns a set of tiles to exclude from an explosion, for use with directional explosions.
        ///     The angle is relative to the grid the explosion is being spawned on.
        /// </summary>
        public HashSet<Vector2i> GetDirectionalRestriction(
            GridId gridId,
            Vector2i epicenter,
            Angle angle,
            float spread,
            float distance)
        {
            var grid = _mapManager.GetGrid(gridId);

            // Our directed explosion MUST have at least one directly adjacent tile it can propagate to. We enforce this by
            // increasing the arc size until it contains a neighbor. If the direction is pointed exactly towards a
            // neighboring tile, then the spread can be arbitrarily small
            var deltaAngle = angle - angle.GetCardinalDir().ToAngle();
            var minSpread = (float) (1 + 2 *Math.Abs(deltaAngle.Degrees));
            spread = Math.Max(spread, minSpread);

            // Get a circle centered on the epicenter, which is used to exclude tiles. The radius of this circle
            // effectively determines "how far" the explosive is directed, before it spreads out normally. If the
            // explosion wraps around this circle, it will look very odd, so it should probably be scaled with the
            // explosion size.
            var circle = new Circle(grid.GridTileToWorldPos(epicenter), distance);

            HashSet<Vector2i> excluded = new();
            foreach (var tileRef in grid.GetTilesIntersecting(circle, ignoreEmpty: false))
            {
                // As we only care about angles, it doesn't matter whether we use vector2i grid indices or Vector2
                // tile-centers to calculate angles.
                var relativeAngle = angle - new Angle(tileRef.GridIndices - epicenter);

                // check whether the tile is outside of the included arc. If so, exclude it.
                if (Math.Abs(relativeAngle.Degrees) * 2 > spread)
                    excluded.Add(tileRef.GridIndices);
            }

            return excluded;
        }

        /// <summary>
        ///     This is the main explosion generating function. 
        /// </summary>
        /// <param name="gridId">The grid where the epicenter tile is located</param>
        /// <param name="epicenterTile">The center of the explosion, specified as a tile index</param>
        /// <param name="intensity">The final sum of the tile intensities. This governs the overall size of the
        /// explosion</param>
        /// <param name="slope">How quickly does the intensity decrease when moving away from the epicenter.</param>
        /// <param name="maxIntensity">The maximum intensity that the explosion can have at any given tile. This
        /// effectively caps the damage that this explosion can do.</param>
        /// <param name="exclude">A set of tiles to exclude from the explosion.</param>
        /// <returns>Returns a list of tile-sets and a list of intensity values which describe the explosion.</returns>
        public (List<HashSet<Vector2i>>, List<float>) GetExplosionTiles(
            GridId gridId,
            Vector2i epicenterTile,
            float intensity,
            float slope,
            float maxIntensity,
            HashSet<Vector2i>? exclude = null)
        {
            var intensityStepSize = slope / 2;

            if (intensity < 0  || intensityStepSize <= 0)
                return (new(), new());

            // This is the list of sets of tiles that will be targeted by our explosions.
            // Here we initialize tileSetList. The first three entries are trivial, but make the following for loop
            // logic neater. ALl things considered, this is a trivial waste of memory.
            List<HashSet<Vector2i>> tileSetList = new();
            tileSetList.Add(new HashSet<Vector2i>());
            tileSetList.Add(new HashSet<Vector2i> { epicenterTile });
            tileSetList.Add(new HashSet<Vector2i>());
            var iteration = 3;

            // is this even a multi-tile explosion?
            if (intensity < slope)
                return (tileSetList, new() { 0, intensity, 0 });

            // List of all tiles in the explosion.
            // Used to avoid explosions looping back in on themselves.
            // Therefore, can also used to exclude tiles
            HashSet<Vector2i> processedTiles = exclude ?? new();
            processedTiles.Add(epicenterTile);
            var tilesInIteration = new List<int> { 0, 1, 0 };
            List<float> tileSetIntensity = new () { 0, slope, 0 };
            float remainingIntensity = intensity - slope;


            // Directional airtight blocking made this all super convoluted. basically: delayedNeighbor is when an
            // explosion cannot LEAVE a tile in a certain direction, while delayedSpreader is when an explosion cannot
            // ENTER a tile and spread outwards from there.

            // Tiles which neighbor an exploding tile, but have not yet had the explosion spread to them due to an
            // airtight entity on the exploding tile that prevents the explosion from spreading in that direction. These
            // will be added as a neighbor after some delay, once the explosion on that tile is sufficiently strong to
            // destroy the airtight entity.
            Dictionary<int, List<(Vector2i, AtmosDirection)>> delayedNeighbors = new();

            // This is a tile which is currently exploding, but has not yet started to spread the explosion to
            // surrounding tiles. This happens if the explosion attempted to enter this tile, and there was some
            // airtight entity on this tile blocking explosions from entering from that direction. Once the explosion is
            // strong enough to destroy this airtight entity, it will begin to spread the explosion to neighbors.
            // This maps an iteration index to a list of delayed spreaders that begin spreading at that iteration.
            Dictionary<int, List<Vector2i>> delayedSpreaders = new();

            // What iteration each delayed spreader originally belong to
            Dictionary<Vector2i, int> delayedSpreaderIteration = new();

            bool exit = false;
            int maxIntensityIndex = 1;
            if (!AirtightMap.TryGetValue(gridId, out var airtightMap))
            {
                airtightMap = new();
            }

            // Main flood-fill loop
            HashSet<Vector2i> newTiles;
            while (remainingIntensity > 0)
            {
                var previousIntensity = remainingIntensity;

                // First we will add a new iteration of tiles
                newTiles = new();
                tileSetList.Add(newTiles);
                tilesInIteration.Add(0);

                // We use the local GetNewTiles function to enumerate over neighbors of tiles that were recently added to tileSetList.
                foreach (var (newTile, direction) in GetNewTiles())
                {
                    // does this new tile have any airtight entities?
                    // note that blockedDirections defaults to 0 (no blocked directions)
                    var (sealIntegrity, blockedDirections) = airtightMap.GetValueOrDefault(newTile);

                    // If the explosion is entering this new tile from an unblocked direction, we add it directly
                    if (!blockedDirections.IsFlagSet(direction.GetOpposite()))
                    {
                        processedTiles.Add(newTile);
                        newTiles.Add(newTile);

                        if (!delayedSpreaderIteration.ContainsKey(newTile))
                            tilesInIteration[^1]++;

                        continue;
                    }

                    if (delayedSpreaderIteration.ContainsKey(newTile))
                        continue;

                    // if this tile is blocked from all directions. then there is no way to snake around and spread
                    // out from it without first breaking it. so we can already mark it as processed for future iterations.
                    if (blockedDirections == AtmosDirection.All)
                        processedTiles.Add(newTile);

                    // At what explosion iteration would this blocker be destroyed?
                    var clearIteration = iteration + (int) MathF.Ceiling(sealIntegrity / intensityStepSize);
                    if (delayedSpreaders.TryGetValue(clearIteration, out var list))
                        list.Add(newTile);
                    else
                        delayedSpreaders[clearIteration] = new() { newTile };

                    delayedSpreaderIteration[newTile] = iteration;
                    tilesInIteration[^1]++;
                }

                // Does adding these tiles bring us above the total target intensity?
                if (tilesInIteration[^1] * intensityStepSize >= remainingIntensity)
                {
                    tileSetIntensity.Add((float) remainingIntensity / tilesInIteration[^1]);
                    break;
                }
                tileSetIntensity.Add(intensityStepSize);
                remainingIntensity -= tilesInIteration[^1] * intensityStepSize;

                // Now that we added a complete new iteration of tiles, we try to increase the intensity of previous
                // iterations.
                for (var i = maxIntensityIndex; i < iteration; i++)
                {
                    if (tilesInIteration[i] * intensityStepSize >= remainingIntensity &&
                        tilesInIteration[i] * (maxIntensity - tileSetIntensity[i]) >= remainingIntensity)
                    {
                        // there is not enough left to distribute. add a fractional amount and break.
                        tileSetIntensity[i] += (float) remainingIntensity / tilesInIteration[i];
                        exit = true;
                        break;
                    }

                    tileSetIntensity[i] += intensityStepSize;
                    remainingIntensity -= tilesInIteration[i] * intensityStepSize;

                    if (tileSetIntensity[i] >= maxIntensity)
                    {
                        // reached max intensity, stop increasing intensity of this tile set and refund some intensity
                        remainingIntensity += tilesInIteration[i] * (tileSetIntensity[i] - maxIntensity);
                        maxIntensityIndex = i + 1;
                        tileSetIntensity[i] = maxIntensity;
                    }
                }
                if (exit) break;

                if (processedTiles.Count >= MaxArea)
                    //Whooo! MAXCAP!
                    break;

                if (remainingIntensity == previousIntensity)
                    // this can only happen if all tiles are at maxTileIntensity and there were no neighbors to expand
                    // to. Given that all tiles are at their maximum damage, no walls will be broken in future
                    // iterations and we can just exit early.
                    break;

                iteration += 1;
            }

            // final cleanup.
            // Here we add delayedSpreaders to tileSetList.
            foreach (var (tile, index) in delayedSpreaderIteration)
            {
                tileSetList[index].Add(tile);
            }

            // Next, we remove duplicate tiles. Currently this can happen when a delayed spreader was circumvented.
            // E.g., a windoor blocked the explosion, but the explosion snaked around and added the tile before the
            // windoor broke.
            processedTiles.Clear();
            foreach (var tileSet in tileSetList)
            {
                tileSet.ExceptWith(processedTiles);
                processedTiles.UnionWith(tileSet);
            }

            return (tileSetList, tileSetIntensity);

            #region Local functions
            // Get all of the new tiles that the explosion will cover in this new iteration.
            IEnumerable<(Vector2i, AtmosDirection)> GetNewTiles()
            {
                // firstly, if any delayed spreaders were cleared, add then to processed tiles to avoid unnecessary
                // calculations
                if (delayedSpreaders.TryGetValue(iteration, out var clearedSpreaders))
                    processedTiles.UnionWith(clearedSpreaders);

                // construct our enumerable from several other iterators
                var enumerable = GetNewAdjacentTiles(tileSetList[iteration - 2]);
                enumerable = enumerable.Concat(GetNewDiagonalTiles(tileSetList[iteration - 3]));
                enumerable = enumerable.Concat(GetDelayedTiles());

                // were there any delayed spreaders that we need to get the neighbors of?
                if (delayedSpreaders.TryGetValue(iteration - 2, out var delayedAdjacent))
                    enumerable = enumerable.Concat(GetNewAdjacentTiles(delayedAdjacent, true));
                if (delayedSpreaders.TryGetValue(iteration - 3, out var delayedDiagonal))
                    enumerable = enumerable.Concat(GetNewDiagonalTiles(delayedDiagonal, true));

                return enumerable;
            }

            IEnumerable<(Vector2i, AtmosDirection)> GetDelayedTiles()
            {
                if (!delayedNeighbors.TryGetValue(iteration, out var delayed))
                    yield break;

                foreach (var tile in delayed)
                {
                    if (!processedTiles.Contains(tile.Item1))
                        yield return tile;
                }

                delayedNeighbors.Remove(iteration);
            }

            // Gets the tiles that are directly adjacent to tiles that were added two iterations ago. If a tile has an
            // airtight entity that blocks the explosion, those tiles are added to a list of delayed tiles that will be
            // added to the explosion in some future iteration.
            IEnumerable<(Vector2i, AtmosDirection)> GetNewAdjacentTiles(IEnumerable<Vector2i> tiles, bool ignoreTileBlockers = false)
            {
                Vector2i newTile;
                foreach (var tile in tiles)
                {
                    // Note that if (grid, tile) is not a valid key, then airtight.BlockedDirections will default to 0 (no blocked directions)
                    var (sealIntegrity, blockedDirections) = airtightMap.GetValueOrDefault(tile);

                    // First, yield any neighboring tiles that are not blocked by airtight entities on this tile
                    for (var i = 0; i < Atmospherics.Directions; i++)
                    {
                        var direction = (AtmosDirection) (1 << i);
                        if (ignoreTileBlockers || !blockedDirections.IsFlagSet(direction))
                        {
                            newTile = tile.Offset(direction);
                            if (!processedTiles.Contains(newTile))
                                yield return (tile.Offset(direction), direction);
                        }
                    }

                    // If there are no blocked directions, we are done with this tile.
                    if (ignoreTileBlockers || blockedDirections == AtmosDirection.Invalid)
                        continue;

                    // This tile has one or more airtight entities anchored to it blocking the explosion from traveling in
                    // some directions. First, check whether this blocker can even be destroyed by this explosion?
                    if (sealIntegrity > maxIntensity || float.IsNaN(sealIntegrity))
                        continue;

                    // At what explosion iteration would this blocker be destroyed?
                    var clearIteration = iteration + (int) MathF.Ceiling(sealIntegrity / intensityStepSize);

                    // We will add this neighbor to delayedTiles instead of yielding it directly during this iteration
                    if (!delayedNeighbors.TryGetValue(clearIteration, out var list))
                    {
                        list = new();
                        delayedNeighbors[clearIteration] = list;
                    }

                    // Check which directions are blocked and add them to the delayed tiles list
                    for (var i = 0; i < Atmospherics.Directions; i++)
                    {
                        var direction = (AtmosDirection) (1 << i);
                        if (blockedDirections.IsFlagSet(direction))
                        {
                            newTile = tile.Offset(direction);
                            if (!processedTiles.Contains(newTile))
                                list.Add((tile.Offset(direction), direction));
                        }
                    }
                }
            }

            // Get the tiles that are diagonally adjacent to the tiles from three iterations ago. Note that if there are
            // ANY air blockers in some direction, that diagonal tiles is not added. The explosion will have to
            // propagate along cardinal directions.
            IEnumerable<(Vector2i, AtmosDirection)> GetNewDiagonalTiles(IEnumerable<Vector2i> tiles, bool ignoreTileBlockers = false)
            {
                Vector2i newTile;
                AtmosDirection direction;
                foreach (var tile in tiles)
                {
                    // Note that if a (grid,tile) is not a valid key, airtight.BlockedDirections defaults to 0 (no blocked directions).
                    var airtight = airtightMap.GetValueOrDefault(tile);
                    var freeDirections = ignoreTileBlockers
                        ? AtmosDirection.All
                        : ~airtight.BlockedDirections;

                    // Get the free directions of the directly adjacent tiles
                    var freeDirectionsN = ~airtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.North)).BlockedDirections;
                    var freeDirectionsE = ~airtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.East)).BlockedDirections;
                    var freeDirectionsS = ~airtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.South)).BlockedDirections;
                    var freeDirectionsW = ~airtightMap.GetValueOrDefault(tile.Offset(AtmosDirection.West)).BlockedDirections;

                    // North East
                    if (freeDirections.IsFlagSet(AtmosDirection.NorthEast))
                    {
                        direction = AtmosDirection.Invalid;
                        if (freeDirectionsN.IsFlagSet(AtmosDirection.SouthEast))
                            direction |= AtmosDirection.East;
                        if (freeDirectionsE.IsFlagSet(AtmosDirection.NorthWest))
                            direction |= AtmosDirection.North;

                        newTile = tile + (1, 1);
                        if (direction != AtmosDirection.Invalid && !processedTiles.Contains(newTile))
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
                        if (direction != AtmosDirection.Invalid && !processedTiles.Contains(newTile))
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
                        if (direction != AtmosDirection.Invalid && !processedTiles.Contains(newTile))
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
                        if (direction != AtmosDirection.Invalid && !processedTiles.Contains(newTile))
                            yield return (newTile, direction);
                    }
                }
            }
            #endregion
        }
    }
}
