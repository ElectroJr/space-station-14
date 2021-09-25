using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Sound;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server.Explosion
{

    //todo make diagonal walls block explosions

    // todo:
    // atmos airtight instead of my thing
    // grid-jump
    // launch direction is gonna be hard


    // Todo create explosion prototypes.
    // E.g. Fireball (heat), AP (heat+piercing), HE (heat+blunt), dirty (heat+radiation)
    // Each explosion type will need it's own threshold map

    // TODO
    // Make explosion progress in steps?
    // Avioids dumping all of the damage change events into a single tick.



    // test going around corners and through walls
    // test different shielding amounts
    // test enclosed spaces cause directional explosions
    // test that a fully enclosed space ramps up damage until the weakest link dies
    // test that an enclosed space with two weak points, with one a bit weaker than the other, both break asymmetrically
    // test girders / ExplosionPassable do not block explosions

    // TODO explosion audio & shake

    public sealed class ExplosionSystem : EntitySystem
    {
        private static SoundSpecifier _explosionSound = new SoundCollectionSpecifier("explosion");

        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IEntityLookup _entityLookup = default!;
        [Dependency] private readonly ExplosionBlockerSystem _explosionBlockerSystem = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly GridTileLookupSystem _gridTileLookupSystem = default!;

        public Queue<Tuple<List<EntityUid>, float>> EntitiesToDamage = new();

        public DamageSpecifier BaseExplosionDamage = new();

        public override void Initialize()
        {
            base.Initialize();

            BaseExplosionDamage.DamageDict = new() { { "Heat", 5 }, { "Blunt", 5 }, { "Piercing", 5 } };
        }

        private void PlaySound(EntityCoordinates coords)
        {

            SoundSystem.Play( Filter.Broadcast(), _explosionSound.GetSound(), coords);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            if (!EntitiesToDamage.TryDequeue(out var tuple))
                return;

            Stopwatch sw = new();
            sw.Start();

            DamageEntities(tuple.Item1, tuple.Item2);

            var time = sw.Elapsed.TotalMilliseconds;

            Logger.Info($"Damage time: {sw.Elapsed.TotalMilliseconds} ms");
        }

        public List<List<EntityUid>> GetEntities(List<HashSet<Vector2i>> tileSetList, IMapGrid grid)
        {
            HashSet<EntityUid> known = new();

            // Associate each entity with a tile generation
            List<List<EntityUid>> result = new();
            foreach (var tileSet in tileSetList)
            {
                // In a circular explosion, this tileSet is a ring of constant distance

                result.Add(new());
                foreach (var tile in tileSet)
                {
                    foreach (var entity in _gridTileLookupSystem.GetEntitiesIntersecting(grid.Index, tile))
                    {
                        if (entity.Transform.ParentUid != grid.GridEntityId)
                            continue;

                        if (known.Contains(entity.Uid))
                            continue;

                        result.Last().Add(entity.Uid);
                        known.Add(entity.Uid);
                    }
                }
            }

            return result;
        }


        public (List<HashSet<Vector2i>>?, List<float>?) GetExplosionTiles(MapCoordinates epicenter, int strength, int damagePerIteration)
        {
            if (strength <= 0)
                return (null, null);

            if (!_mapManager.TryFindGridAt(epicenter, out var grid))
                return (null, null);

            var epicenterTile = grid.TileIndicesFor(epicenter);

            return GetExplosionTiles(grid, epicenterTile, strength, damagePerIteration);
        }

        public (List<HashSet<Vector2i>>?, List<float>?) GetDirectedExplosionTiles(
            IMapGrid grid,
            Vector2i tile,
            int totalStrength,
            int damagePerIteration,
            Angle fanDirection,
            int fanWidthDegrees,
            int fanRadius)
        {
            var gridXform = ComponentManager.GetComponent<ITransformComponent>(grid.GridEntityId);
            var center = gridXform.WorldMatrix.Transform((Vector2) tile + 0.5f);
            var circle = new Circle(center, fanRadius);

            HashSet<Vector2i> excluded = new();
            foreach (var tileRef in grid.GetTilesIntersecting(circle, ignoreEmpty: false))
            {
                var otherCenter = gridXform.WorldMatrix.Transform((Vector2) tileRef.GridIndices + 0.5f);

                var angle = fanDirection - new Angle(center - otherCenter);
                if (Math.Abs(angle.Degrees) * 2 > fanWidthDegrees)
                    excluded.Add(tileRef.GridIndices);
            }

            return GetExplosionTiles(grid, tile, totalStrength, damagePerIteration, excluded);
        }

        public (List<HashSet<Vector2i>>?, List<float>?) GetExplosionTiles(IMapGrid grid, Vector2i epicenterTile, int remainingStrength, int damagePerIteration, HashSet<Vector2i>? encounteredTiles = null)
        {
            if (remainingStrength < 1 || damagePerIteration < 1)
                return (null, null);

            // A sorted list of sets of tiles that will be targeted by explosions.
            List<HashSet<Vector2i>> explodedTiles = new();
            // Each set of tiles receives the same explosion intensity.
            // The order in which the sets appear in the list corresponds to the "effective distance" to the epicenter (walls increase effective distance).

            // The "distance" is related to the list index via: distance = -0.5 +(index/2)

            // The set of all tiles that will be targeted by this explosion.
            // This is used to stop adding the same tile twice if an explosion loops around an obstacle / encounters itself.
            encounteredTiles ??= new();
            encounteredTiles.Add(epicenterTile);

            // A queue of tiles that are receiving damage, but will only let the explosion spread to neighbors after some delay.
            // The delay duration depends on
            Dictionary<int, Dictionary<Vector2i, int>> blockedTiles = new();

            // Initialize list with some sets. The first three entries are trivial, but make the following for loop
            // logic nicer. Some of these will be filled in during the iteration.
            explodedTiles.Add(new HashSet<Vector2i>());
            explodedTiles.Add(new HashSet<Vector2i> { epicenterTile });
            explodedTiles.Add(new HashSet<Vector2i>());

            var strengthPerIteration = new List<float> { 2, 1, 0 };
            var tilesInIteration = new List<int> { 0, 1, 0 };

            var iteration = 3;// the tile set iteration we are CURRENTLY adding in every loop
            HashSet<Vector2i> adjacentTiles, diagonalTiles;
            Dictionary< Vector2i, int> impassableTiles;
            Dictionary<Vector2i, int>? clearedTiles;
            bool done = false;
            remainingStrength--;
            while (remainingStrength > 0)
            {
                // get the iterator that tells us what tiles we want to find the adjacent neighbors of. usually this is just
                // explodedTiles[index], but it's possible a wall was destroyed and we want to start adding it's
                // neighbors.
                IEnumerable<Vector2i> adjacentIterator = blockedTiles.TryGetValue(iteration - 2, out clearedTiles)
                    ? explodedTiles[iteration - 2].Concat(clearedTiles.Keys)
                    : explodedTiles[iteration - 2];

                adjacentTiles = GetAdjacentTiles(adjacentIterator, encounteredTiles);

                // Does this bring us over the damage limit?
                if (adjacentTiles.Count >= remainingStrength)
                {
                    strengthPerIteration.Add((float) remainingStrength / adjacentTiles.Count());
                    explodedTiles.Add(adjacentTiles);
                    break;
                }

                tilesInIteration.Add(adjacentTiles.Count);
                strengthPerIteration.Add(1);
                remainingStrength -= adjacentTiles.Count;
                encounteredTiles.UnionWith(adjacentTiles);

                // check if any of the new tiles are impassable.
                impassableTiles = GetImpassableTiles(adjacentTiles, grid.Index);
                adjacentTiles.ExceptWith(impassableTiles.Keys);
                explodedTiles.Add(adjacentTiles);

                // add impassable delays to the set of blocked tiles.
                // these tiles will be added to some future iteration.
                foreach (var (tile, tolerance) in impassableTiles)
                {
                    // How many iterations later would this tile become passable (i.e., when is the wall destroyed and
                    // the explosion can propagate)?

                    var delay = - 1 + (int) Math.Ceiling((float) tolerance / damagePerIteration);
                    // (- 1 + ... ) because if a single set of explosion damage is enough to kill this obstacle, there is no delay.

                    // Add these tiles to some delayed future iteration
                    if (blockedTiles.ContainsKey(iteration + delay))
                        blockedTiles[iteration + delay].Add(tile, iteration);
                    else
                        blockedTiles.Add(iteration + delay, new() { { tile, iteration } });
                }

                // Next, repeat but get the tiles that should explode due to diagonal adjacency
                IEnumerable<Vector2i> diagonalIterator = blockedTiles.TryGetValue(iteration - 3, out clearedTiles)
                    ? explodedTiles[iteration - 3].Concat(clearedTiles.Keys)
                    : explodedTiles[iteration - 3];

                diagonalTiles = GetDiagonalTiles(diagonalIterator, encounteredTiles);

                // Does this bring us over the damage limit?
                if (diagonalTiles.Count >= remainingStrength)
                {
                    // add this as a new iteration with fractional damage
                    strengthPerIteration.Add((float) remainingStrength / diagonalTiles.Count());
                    explodedTiles.Add(diagonalTiles);
                    break;
                }

                tilesInIteration[iteration] += diagonalTiles.Count;
                remainingStrength -= diagonalTiles.Count;
                encounteredTiles.UnionWith(diagonalTiles);

                // check if any of the new tiles are impassable
                impassableTiles = GetImpassableTiles(diagonalTiles, grid.Index);
                diagonalTiles.ExceptWith(impassableTiles.Keys);
                explodedTiles.Last().UnionWith(diagonalTiles);

                // add impassable delays to the set of blocked tiles.
                // these tiles will be added to some future iteration.
                foreach (var (tile, tolerance) in impassableTiles)
                {
                    // How many iterations later would this tile become passable (i.e., when is the wall destroyed and
                    // the explosion can propagate)?

                    var delay = (int) Math.Ceiling((float) tolerance / damagePerIteration);

                    // Add these tiles to some delayed future iteration
                    if (blockedTiles.ContainsKey(iteration + delay))
                        blockedTiles[iteration + delay].Add(tile, iteration);
                    else
                        blockedTiles.Add(iteration + delay, new() { { tile, iteration } });
                }

                // then for each previous iteration, we start increasing the damage.
                for (var i = 1; i < iteration; i++)
                {

                    if (tilesInIteration[i] >= remainingStrength)
                    {
                        // there is not enough damage left. add a fraction amount and break.
                        strengthPerIteration[i] += (float) remainingStrength / tilesInIteration[i];
                        done = true;
                        break;
                    }

                    remainingStrength -= tilesInIteration[i];
                    strengthPerIteration[i]++;
                }
                if (done) break;

                iteration += 1;
            }

            // add the delayed tiles back into the main list for damage calculations
            foreach (var value in blockedTiles.Values)
            {
                foreach (var (tile, originalIteration) in value)
                {
                    explodedTiles[originalIteration].Add(tile);
                }
            }

            return (explodedTiles, strengthPerIteration);
        }

        public void SpawnExplosion(IMapGrid grid, Vector2i epicenterTile, int remainingStrength, int damagePerIteration, HashSet<Vector2i>? encounteredTiles = null)
        {
            var (explodedTiles, strength) = GetExplosionTiles(grid, epicenterTile, remainingStrength, damagePerIteration, encounteredTiles);

            if (explodedTiles == null)
                return;

            int a = 0;
            foreach (var e in GetEntities(explodedTiles, grid))
            {
                EntitiesToDamage.Enqueue(Tuple.Create(e, strength![a]*damagePerIteration));
            }
        }

        public void DamageEntities(List<List<EntityUid>> entityLists, List<float> intensityList, int damageScale)
        {
            int i = 0;
            foreach (var list in entityLists)
            {
                DamageEntities(list, damageScale * intensityList[i]);
                i++;
            }
        }

        public void DamageEntities(List<EntityUid> entities, float scale)
        {
            var damage = BaseExplosionDamage * scale;
            foreach (var entity in entities)
            {
                _damageableSystem.TryChangeDamage(entity, damage);
            }
        }

        /// <summary>
        ///     Given a set of tiles, get a list of the ones that are impassable to explosions.
        /// </summary>
        private Dictionary<Vector2i, int> GetImpassableTiles(HashSet<Vector2i> tiles, GridId grid)
        {
            Dictionary<Vector2i, int> impassable = new();
            if (!_explosionBlockerSystem.BlockerMap.TryGetValue(grid, out var tileTolerances))
                return impassable;

            foreach (var tile in tiles)
            {
                if (!tileTolerances.TryGetValue(tile, out var tolerance))
                    continue;

                if (tolerance == 0)
                    continue;

                impassable.Add(tile, tolerance);
            }

            return impassable;
        }

        private HashSet<Vector2i> GetAdjacentTiles(IEnumerable<Vector2i> tiles, HashSet<Vector2i> existingTiles)
        {
            HashSet<Vector2i> adjacentTiles = new();
            foreach (var tile in tiles)
            {
                // Hashset question: Is it better to:
                //      A) create a HashSet of tiles, then do ExceptWith after finishing adding all elements
                //      B) only add to a HashSet if the new member is not in the intersection?
                // A) probably has more allocating, but maybe however HashSet intersections are done is inherently faster?
                // So lets use A) for now....
                adjacentTiles.Add(tile + (0, 1));
                adjacentTiles.Add(tile + (1, 0));
                adjacentTiles.Add(tile + (0, -1));
                adjacentTiles.Add(tile + (-1, 0));
            }

            adjacentTiles.ExceptWith(existingTiles);
            return adjacentTiles;
        }

        private HashSet<Vector2i> GetDiagonalTiles(IEnumerable<Vector2i> tiles, HashSet<Vector2i> existingTiles)
        {
            HashSet<Vector2i> diagonalTiles = new();
            foreach (var tile in tiles)
            {
                diagonalTiles.Add(tile + (1, 1));
                diagonalTiles.Add(tile + (1, -1));
                diagonalTiles.Add(tile + (-1, 1));
                diagonalTiles.Add(tile + (-1, -1));
            }

            diagonalTiles.ExceptWith(existingTiles);
            return diagonalTiles;
        }
    }
}
