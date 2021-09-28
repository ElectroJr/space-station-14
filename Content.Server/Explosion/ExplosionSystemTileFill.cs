using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.GameObjects;
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
        public (List<HashSet<Vector2i>>?, List<float>?) GetExplosionTiles(MapCoordinates epicenter, float totalIntensity, float slope, int maxTileIntensity)
        {
            if (totalIntensity <= 0)
                return (null, null);

            if (!_mapManager.TryFindGridAt(epicenter, out var grid))
                return (null, null);

            var epicenterTile = grid.TileIndicesFor(epicenter);

            return GetExplosionTiles(grid, epicenterTile, totalIntensity, slope, maxTileIntensity);
        }

        /// <summary>
        ///     Variant of GetExplosionTiles that excludes certain tiles, effectively restricting the direction that the
        ///     explosion can travel in.
        /// </summary>
        /// <remarks>
        ///     This literally just excludes tiles in a circle around the epicenter, except for some tiles in a given
        ///     arc. Note that directed explosion can be MUCH more destructive than free explosions with the same
        ///     strength. They will have a much easier time getting through reinforced walls.
        /// </remarks>
        public (List<HashSet<Vector2i>>?, List<float>?) GetDirectedExplosionTiles(
            IMapGrid grid,
            Vector2i epicenter,
            int totalIntensity,
            float slope,
            int maxTileIntensity,
            Angle direction,
            int spreadDegrees = 46,
            int directionalRadius = 5)
        {
            // Our directed explosion MUST have at least one neighboring tile it can propagate to. We enforce this by
            // increasing the arc size until it contains a neighbor. If the direction is pointed exactly towards a
            // neighboring tile, then the spread can be arbitrarily small
            direction = direction.FlipPositive();
            var degreesToNearestNeighbour = Math.Abs(direction.Degrees % 45 - 22.5f);
            spreadDegrees = Math.Max(spreadDegrees, (int) (1 + degreesToNearestNeighbour) * 2);

            // Get a circle centered on the epicenter, which is used to exclude tiles. The radius of this circle
            // effectively determines "how far" the explosive is directed, before it spreads out normally. If the
            // explosion wraps around this circle, it will look very odd, so it should probably be scaled with the
            // explosion size.
            var circle = new Circle(grid.GridTileToWorldPos(epicenter), directionalRadius);

            HashSet<Vector2i> excluded = new();
            foreach (var tileRef in grid.GetTilesIntersecting(circle, ignoreEmpty: false))
            {
                // As we only care about angles, it doesn't matter whether we use vector2i grid indices or Vector2
                // tile-centers to calculate angles.
                var relativeAngle = direction - new Angle(tileRef.GridIndices - epicenter);

                // check whether the tile is outside of the included arc. If so, exclude it.
                if (Math.Abs(relativeAngle.Degrees) * 2 > spreadDegrees)
                    excluded.Add(tileRef.GridIndices);
            }

            return GetExplosionTiles(grid, epicenter, totalIntensity, slope, maxTileIntensity, excluded);
        }

        /// <summary>
        ///     This is the main explosion generating function. 
        /// </summary>
        /// <param name="grid">The grid where the epicenter tile is located</param>
        /// <param name="epicenterTile">The center of the explosion, specified as a tile index</param>
        /// <param name="intensity">The final sum of the tile intensities. This governs the overall size of the
        /// explosion</param>
        /// <param name="intensitySlope">How quickly does the intensity decrease when moving away from the epicenter.</param>
        /// <param name="maxTileIntensity">The maximum intensity that the explosion can have at any given tile. This
        /// effectively caps the damage that this explosion can do.</param>
        /// <param name="exclude">A set of tiles to exclude from the explosion.</param>
        /// <returns>Returns a list of tile-sets and a list of intensity values which describe the explosion.</returns>
        public (List<HashSet<Vector2i>>, List<float>) GetExplosionTiles(
            IMapGrid grid,
            Vector2i epicenterTile,
            float intensity,
            float intensitySlope,
            int maxTileIntensity,
            HashSet<Vector2i>? exclude = null)
        {
            var intensityStepSize = intensitySlope / 2;

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
            if (intensity < intensitySlope)
                return (tileSetList, new() { 0, intensity, 0 });

            // List of all tiles in the explosion.
            // Used to avoid explosions looping back in on themselves.
            // Therefore, can also used to exclude tiles
            HashSet<Vector2i> allTiles = exclude ?? new();
            allTiles.Add(epicenterTile);

            List<float> tileSetIntensity = new () { 0, intensitySlope, 0 };

            // Keep track of the number of tiles in each tileSet. Tiles with walls/obstacles are not directly added to
            // `explodedTiles`, so we cannot just use the count function.
            var tilesInIteration = new List<int> { 0, 1, 0 };

            // Tiles with obstacles are added to this dictionary, instead of directly to tileSetList. The key
            // corresponds to the explosion iteration at which a collection of tiles become un-blocked. The value is
            // another dictionary, which maps the tiles to the tileSetList index that they would have originally been
            // added to if they weren't blocked.
            Dictionary<int, Dictionary<Vector2i, int>> delayedTiles = new();



            HashSet<Vector2i> adjacentTiles, diagonalTiles;
            Dictionary<Vector2i, int> impassableTiles;
            Dictionary<Vector2i, int>? clearedTiles;
            bool exit = false;
            int maxIntensityIndex = 1;
            float remainingIntensity = intensity - intensitySlope;

            // Main flood-fill loop
            while (remainingIntensity > 0)
            {
                var previousIntensity = remainingIntensity;

                // First, we want to fill in the tiles that are adjacent to those that were added two iterations ago.
                adjacentTiles = new(GetAdjacentTiles(tileSetList[iteration - 2]));

                // We also want to add any neighbors of tiles that were previously blocked, but were cleared/destroyed
                // two generations ago.
                if (delayedTiles.TryGetValue(iteration - 2, out clearedTiles))
                    adjacentTiles.UnionWith(GetAdjacentTiles(clearedTiles.Keys));

                // Then we remove any previously encountered tiles. This avoids the explosion looping back on itself
                adjacentTiles.ExceptWith(allTiles);

                // Does adding these tiles bring us above the total target intensity?
                if (adjacentTiles.Count * intensityStepSize >= remainingIntensity)
                {
                    tileSetIntensity.Add((float) remainingIntensity / adjacentTiles.Count());
                    tileSetList.Add(adjacentTiles);
                    break;
                }
 
                tilesInIteration.Add(adjacentTiles.Count);
                tileSetIntensity.Add(intensityStepSize); 
                remainingIntensity -= adjacentTiles.Count * intensityStepSize;
                allTiles.UnionWith(adjacentTiles);

                // check if any of the new tiles are impassable.
                impassableTiles = GetImpassableTiles(adjacentTiles, grid.Index);
                AddDelayedTiles(impassableTiles, delayedTiles, iteration, intensityStepSize, maxTileIntensity);

                // add the free tiles to the main tile-set list
                adjacentTiles.ExceptWith(impassableTiles.Keys);
                tileSetList.Add(adjacentTiles);

                // Next, we do the same as above but for diagonal tiles that were added 3 iterations ago.
                // This represents the fact that diagonal tiles are 1.5* as far away as directly adjacent tiles.
                // Everyone knows sqrt(2) == 1.5 exactly.
                diagonalTiles = new(GetDiagonalTiles(tileSetList[iteration - 3]));
                if (delayedTiles.TryGetValue(iteration - 3, out clearedTiles))
                    diagonalTiles.UnionWith(GetAdjacentTiles(clearedTiles.Keys));

                diagonalTiles.ExceptWith(allTiles);
                if (diagonalTiles.Count * intensityStepSize >= remainingIntensity)
                {
                    // add as a NEW iteration with fractional damage, in order to keep separate from adjacent tiles,
                    // which have integer damage.
                    tileSetIntensity.Add((float) remainingIntensity / diagonalTiles.Count());
                    tileSetList.Add(diagonalTiles);
                    break;
                }

                // add diagonal tiles to the set of adjacent tiles.
                tilesInIteration[iteration] += diagonalTiles.Count;
                remainingIntensity -= diagonalTiles.Count * intensityStepSize;
                allTiles.UnionWith(diagonalTiles);
                impassableTiles = GetImpassableTiles(diagonalTiles, grid.Index);
                AddDelayedTiles(impassableTiles, delayedTiles, iteration, intensityStepSize, maxTileIntensity);
                diagonalTiles.ExceptWith(impassableTiles.Keys);
                tileSetList.Last().UnionWith(diagonalTiles);

                // Now that we added a complete new iteration of tiles, we try to  increase the intensity of previous
                // iterations by 1.
                for (var i = maxIntensityIndex; i < iteration; i++)
                {
                    if (tilesInIteration[i] * intensityStepSize >= remainingIntensity &&
                        tilesInIteration[i] * (maxTileIntensity - tileSetIntensity[i]) >= remainingIntensity)
                    {
                        // there is not enough left to distribute. add a fractional amount and break.
                        tileSetIntensity[i] += (float) remainingIntensity / tilesInIteration[i];
                        exit = true;
                        break;
                    }

                    tileSetIntensity[i] += intensityStepSize;
                    remainingIntensity -= tilesInIteration[i] * intensityStepSize;

                    if (tileSetIntensity[i] >= maxTileIntensity)
                    {
                        // reached max intensity, stop increasing intensity of this tile set and refund some intensity
                        remainingIntensity += tilesInIteration[i] * (tileSetIntensity[i] - maxTileIntensity);
                        maxIntensityIndex = i + 1;
                        tileSetIntensity[i] = maxTileIntensity;
                    }
                        

                }
                if (exit) break;

                if (allTiles.Count >= MaxArea)
                    //Whooo! MAXCAP!
                    break;

                if (remainingIntensity == previousIntensity)
                    // this can only happen if all tiles are at maxTileIntensity & there are no neighbors to expand to.
                    break;

                iteration += 1;
            }

            // The main flood-fill has completed. To finish up, we add the delayed tiles back into the main tile-set list
            foreach (var value in delayedTiles.Values)
            {
                foreach (var (tile, originalIteration) in value)
                {
                    tileSetList[originalIteration].Add(tile);
                }
            }

            return (tileSetList, tileSetIntensity);
        }

        /// <summary>
        ///     Given a list of blocked tiles, determine at what explosion intensity (and thus tile iteration) they
        ///     become unblocked. These are then added to the delayed-tile dictionary.
        /// </summary>
        private void AddDelayedTiles(
            Dictionary<Vector2i, int> impassableTiles,
            Dictionary<int, Dictionary<Vector2i, int>> delayedTiles,
            int iteration,
            float tileIntensityChangePerIteration,
            int maxTileIntensity)
        {
            foreach (var (tile, sealIntegrity) in impassableTiles)
            {
                // What intensity of explosion is needed to destroy this entity?
                var intensityNeeded = (int) Math.Ceiling((float) sealIntegrity);

                // at what iteration is this tile cleared?
                int clearIteration = (intensityNeeded > maxTileIntensity)
                    ? -1 //never
                    : iteration + (int) MathF.Ceiling(sealIntegrity / tileIntensityChangePerIteration);

                // Add these tiles to some delayed future iteration
                if (delayedTiles.ContainsKey(clearIteration))
                    delayedTiles[clearIteration].Add(tile, iteration);
                else
                    delayedTiles.Add(clearIteration, new() { { tile, iteration } });
            }
        }

        /// <summary>
        ///     Given a set of tiles, get a list of the ones that are impassable to explosions.
        /// </summary>
        private Dictionary<Vector2i, int> GetImpassableTiles(HashSet<Vector2i> tiles, GridId grid)
        {
            Dictionary<Vector2i, int> impassable = new();
            if (!_explosionBlockerSystem.BlockerMap.TryGetValue(grid, out var tileSealIntegrity))
                return impassable;

            foreach (var tile in tiles)
            {
                if (!tileSealIntegrity.TryGetValue(tile, out var sealIntegrity))
                    continue;

                if (sealIntegrity == 0)
                    continue;

                impassable.Add(tile, sealIntegrity);
            }

            return impassable;
        }

        private IEnumerable<Vector2i> GetAdjacentTiles(IEnumerable<Vector2i> tiles)
        {
            foreach (var tile in tiles)
            {
                yield return tile + (0, 1);
                yield return tile + (1, 0);
                yield return tile + (0, -1);
                yield return tile + (-1, 0);
            }
        }

        private IEnumerable<Vector2i> GetDiagonalTiles(IEnumerable<Vector2i> tiles)
        {
            foreach (var tile in tiles)
            {
                yield return tile + (1, 1);
                yield return tile + (1, -1);
                yield return tile + (-1, 1);
                yield return tile + (-1, -1);
            }
        }
    }
}
