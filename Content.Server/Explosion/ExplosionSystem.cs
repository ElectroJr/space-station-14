using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Camera;
using Content.Server.Explosion.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Throwing;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.Maps;
using Content.Shared.Sound;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server.Explosion
{
    // Explosion grid hop.
    // First note: IF an explosion is CONSTRAINED then it will deal more damage on average to the tiles it does have (AKA: it will have MORE ITERATIONS and a HIGHER MAX INTENSITTY).
    // Conversely, if an explosion is free, it will always deal less damage, have fewer iterations, and a lower max intensity.
    //
    // Given that during the initial explosion iteration, if the explosion spreads over another grid, it propagates FREELY, this means that it NECESSARILY will have fewer overall iterations than it would otherwise.
    // or put another way: IF we were to properly do a dynamic grid hop, unless that grid was COMPLETELY free of obstacles, it would result in fewer iterations.
    //
    // So: making a grid hop happen after the explosion has finishes propagating, and then just seeding a SECONDARY explosion, limited by the # of iterations rather than total strength, will ALWAYS deal less damaage over all.
    //
    // Is this a bad thing? I would argue no. A bit of separation between the grids --> explosion leaks into space--> less constrained --> less damage
    // it's realistic, so fuck it just do it like that

    // todo:
    // grid-jump
    // better admin gui

    // Todo create explosion prototypes.
    // E.g. Fireball (heat), AP (heat+piercing), HE (heat+blunt), dirty (heat+radiation)
    // Each explosion type will need it's own threshold map

    public sealed partial class ExplosionSystem : EntitySystem
    {
        private SoundSpecifier _explosionSound = default!;
        private AudioParams _explosionSoundParams = default!;

        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IRobustRandom _robustRandom = default!;
        [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly ContainerSystem _containerSystem = default!;
        [Dependency] private readonly NodeGroupSystem _nodeGroupSystem = default!;

        public const int MaxTilesPerTick = 50;

        /// <summary>
        ///     Queue for delayed processing of explosions.
        /// </summary>
        private Queue<Explosion> _explosions = new();

        public DamageSpecifier DefaultExplosionDamage = new();

        public override void Initialize()
        {
            base.Initialize();

            _explosionSoundParams  = AudioParams.Default.WithAttenuation(Attenuation.InverseDistanceClamped);
            _explosionSoundParams.RolloffFactor = 5f; // Why does attenuation not work??
            _explosionSoundParams.Volume = 0;

            // TODO EXPLOSIONS change volume based on intensity? Given that explosions deal damage iteratively, maybe
            // also match sound duration or modulate the sound as the explosion progresses?

            // TODO YAML prototypes
            _explosionSound = new SoundCollectionSpecifier("explosion");
            DefaultExplosionDamage.DamageDict = new() { { "Heat", 5 }, { "Blunt", 5 }, { "Piercing", 5 } };
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            int tilesToProcess = MaxTilesPerTick;
            while (tilesToProcess > 0 && _explosions.TryPeek(out var explosion))
            {
                var processed = ProcessExplosion(explosion, tilesToProcess);
                tilesToProcess -= processed;

                if (processed == 0)
                    _explosions.Dequeue();
            }

            if (_explosions.Count == 0)
            {
                //wakey wakey
                _nodeGroupSystem.Snoozing = false;
                RaiseNetworkEvent(new ExplosionUpdateEvent(int.MaxValue));
            }
        }

        /// <summary>
        ///     Deal damage, throw entities, and break tiles. Returns the number tiles that were processed.
        /// </summary>
        private int ProcessExplosion(Explosion explosion, int tilesToProcess)
        {
            var processedTiles = 0;

            List<(Vector2i, Tile)> damagedTiles = new();

            foreach (var (tileIndices, intensity, damage) in explosion)
            {
                var tilePosLocal = (Vector2) tileIndices + 0.5f * explosion.Grid.TileSize;

                if (explosion.Grid.TryGetTileRef(tileIndices, out var tileRef))
                {
                    ExplodeTile(explosion.GridLookup, explosion.Grid, tileIndices, intensity, damage, explosion.Epicenter, explosion.ProcessedEntities);
                    DamageFloorTile(tileRef, intensity, damagedTiles);
                }

                ExplodeSpace(explosion.MapLookup, explosion.Grid, tilePosLocal, intensity, damage, explosion.Epicenter, explosion.ProcessedEntities);

                processedTiles++;
                if (processedTiles == tilesToProcess)
                    break;
            }

            RaiseNetworkEvent(new ExplosionUpdateEvent(explosion.TileIteration));

            explosion.Grid.SetTiles(damagedTiles);

            return processedTiles;
        }

        /// <summary>
        ///     Find the strength needed to generate an explosion of a given radius
        /// </summary>
        /// <remarks>
        ///     This assumes the explosion is in a vacuum / unobstructed. Given that explosions are not perfectly
        ///     circular, here radius actually means the sqrt(Area/(2*pi)), where the area is the total number of tiles
        ///     covered by the explosion. Until you get to radius 30+, this is functionally equivalent to the
        ///     actual radius.
        /// </remarks>
        public static float RadiusToIntensity(float radius, float slope, float maxIntensity = 0)
        {
            // This formula came from fitting data, but if you want an intuitive explanation, then consider the
            // intensity of each tile in an explosion to be a height. Then a circular explosion is shaped like a cone.
            // So total intensity is like the volume of a cone with height = slope * radius. Of course, as the
            // explosions are not perfectly circular, this formula isn't perfect, but the formula works reasonably well.

            var coneVolume = slope * MathF.PI / 3 * MathF.Pow(radius, 3);

            if (maxIntensity <= 0 || slope * radius < maxIntensity)
                return coneVolume;

            // This explosion is limited by the maxIntensity.
            // Instead of a cone, we have a conical frustum.

            // Subtract the volume of the missing cone segment, with height:
            var h =  slope * radius - maxIntensity;
            return coneVolume - (h * MathF.PI / 3 * MathF.Pow(h / slope, 2));
        }

        public void SpawnExplosion(EntityUid uid, float intensity, float slope, float maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            if (!EntityManager.TryGetComponent(uid, out ITransformComponent? transform))
                return;

            if (!_mapManager.TryGetGrid(transform.GridID, out var grid))
                return;

            SpawnExplosion(grid, grid.TileIndicesFor(transform.Coordinates), intensity, slope, maxTileIntensity, excludedTiles);
        }

        public void SpawnExplosion(GridId gridId, Vector2i epicenter, float totalIntensity, float slope, float maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            var (tileSetList, tileSetIntensity) = GetExplosionTiles(gridId, epicenter, totalIntensity, slope, maxTileIntensity, excludedTiles);

            if (tileSetList == null)
                return;

            var grid = _mapManager.GetGrid(gridId);

            // Wow dem graphics
            RaiseNetworkEvent(new ExplosionEvent(grid.GridTileToWorld(epicenter), tileSetList, tileSetIntensity, gridId));

            // sound & screen shake
            var range = 3 * (tileSetList.Count - 2);
            var filter = Filter.Empty().AddInRange(grid.GridTileToWorld(epicenter), range);
            SoundSystem.Play(filter, _explosionSound.GetSound(), _explosionSoundParams.WithMaxDistance(range));
            CameraShakeInRange(filter, grid.GridTileToWorld(epicenter), totalIntensity);

            _explosions.Enqueue(new Explosion(
                                    tileSetList,
                                    tileSetIntensity!,
                                    grid,
                                    grid.GridTileToWorld(epicenter),
                                    DefaultExplosionDamage));

            // just a lil nap
            //_nodeGroupSystem.Snoozing = true;
        }

        public void SpawnExplosion(IMapGrid grid, Vector2i epicenter, float totalIntensity, float slope, float maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            var (tileSetList, tileSetIntensity) = GetExplosionTiles(grid.Index, epicenter, totalIntensity, slope, maxTileIntensity, excludedTiles);

            if (tileSetList == null)
                return;

            // Wow dem graphics
            RaiseNetworkEvent(new ExplosionEvent(grid.GridTileToWorld(epicenter), tileSetList, tileSetIntensity, grid.Index));

            // sound & screen shake
            var range = 3*(tileSetList.Count-2);
            var filter = Filter.Empty().AddInRange(grid.GridTileToWorld(epicenter), range);
            SoundSystem.Play(filter, _explosionSound.GetSound(), _explosionSoundParams.WithMaxDistance(range));
            CameraShakeInRange(filter, grid.GridTileToWorld(epicenter), totalIntensity);

            _explosions.Enqueue(new Explosion(
                                    tileSetList,
                                    tileSetIntensity!,
                                    grid,
                                    grid.GridTileToWorld(epicenter),
                                    DefaultExplosionDamage));

            // just a lil nap
            _nodeGroupSystem.Snoozing = true;
        }

        private void CameraShakeInRange(Filter filter, MapCoordinates epicenter, float totalIntensity)
        {
            foreach (var player in filter.Recipients)
            {
                if (player.AttachedEntity == null)
                    continue;

                if (!EntityManager.TryGetComponent(player.AttachedEntity.Uid, out CameraRecoilComponent? recoil))
                    continue;

                var playerPos = player.AttachedEntity.Transform.WorldPosition;
                var delta = epicenter.Position - playerPos;

                if (delta.EqualsApprox(Vector2.Zero))
                    delta = new(0.01f, 0);

                var distance = delta.Length;
                var effect = (int) (5*Math.Pow(totalIntensity,0.5) * (1 / (1 + distance)));
                if (effect > 0.01f)
                {
                    var kick = - delta.Normalized * effect;
                    recoil.Kick(kick);
                }
            }
        }

        /// <summary>
        ///     Every time a tile is broken, the intensity is reduced by this much and the tile-break chance is re-rolled.
        /// </summary>
        /// <remarks>
        ///     Effectively, in order for an explosion to have a chance of double-breaking a tile, the intensity needs
        ///     to be larger than this value. As tile breaking will eventually lead to space tiles & a vacuum forming, this
        ///     number should not be set too small. Otherwise even small explosions could punch a hole through the
        ///     station.
        /// </remarks>
        public const float TileBreakIntensityDecrease = 12f;

        /// <summary>
        ///     Explosion intensity dependent chance for a tile to break down to some base turf.
        /// </summary>
        private float TileBreakChance(float intensity)
        {
            // ~ 10% at intensity 4, 90% at intensity ~12.5
            return (intensity < 1) ? 0 : (1 + MathF.Tanh(intensity/4 - 2)) / 2;
        }

        /// <summary>
        ///     Tries to damage the FLOOR TILE. Not to be confused with damaging / affecting entities intersecting the tile.
        /// </summary>
        public void DamageFloorTile(TileRef tileRef, float intensity, List<(Vector2i GridIndices, Tile Tile)> damagedTiles)
        {
            if (tileRef.Tile.IsEmpty || tileRef.IsBlockedTurf(false))
                return;

            var tileDef = _tileDefinitionManager[tileRef.Tile.TypeId];

            while (_robustRandom.Prob(TileBreakChance(intensity)))
            {
                intensity -= TileBreakIntensityDecrease;

                if (tileDef is not ContentTileDefinition contentTileDef)
                    break;

                // does this have a base-turf that we can break it down to?
                if (contentTileDef.BaseTurfs.Count == 0)
                    break;

                // randomly select a new tile
                tileDef = _tileDefinitionManager[_robustRandom.Pick(contentTileDef.BaseTurfs)];
            }

            if (tileDef.TileId == tileRef.Tile.TypeId)
                return;

            damagedTiles.Add((tileRef.GridIndices, new Tile(tileDef.TileId)));
        }

        private void ThrowEntity(IEntity entity, MapCoordinates epicenter, float intensity)
        {
            if (!entity.HasComponent<ExplosionLaunchedComponent>())
                return;

            var location = entity.Transform.Coordinates.ToMap(EntityManager);
            var throwForce = 10 * MathF.Sqrt(intensity);

            entity.TryThrow(location.Position - epicenter.Position, throwForce);
        }

        /// <summary>
        ///     Find entities on a tile using GridTileLookupSystem and apply explosion effects. Will also try to damage
        ///     the tile's themselves (damage the floor of a grid).
        /// </summary>
        public void ExplodeTile(EntityLookupComponent lookup, IMapGrid grid, Vector2i tile, float intensity, DamageSpecifier damage, MapCoordinates epicenter, HashSet<EntityUid> processed)
        {
            var gridBox =  Box2.UnitCentered.Translated((Vector2) tile + 0.5f * grid.TileSize);

            // TODO EXPLOSION remove list use other func (fast intersecting or whatever)
            List<IEntity> list = new();

            lookup.Tree.QueryAabb(ref list, (ref List<IEntity> list, in IEntity ent) =>
            {
                if (!ent.Deleted && !processed.Contains(ent.Uid) && !_containerSystem.IsEntityInContainer(ent.Uid,ent.Transform))
                    list.Add(ent);
                return true;
            }, gridBox);

            foreach (var uid in grid.GetAnchoredEntities(tile))
            {
                if (EntityManager.TryGetEntity(uid, out var ent))
                    list.Add(ent);
            }
            foreach (var ent in list)
            {
                _damageableSystem.TryChangeDamage(ent.Uid, damage);
            }

            // TODO EXPLOSIONS PERFORMANCE Here, we get the intersecting entities AGAIN for throwing. This way, glass
            // shards spawned from windows will be flung outwards, and not stay where they spawned. BUT this is also
            // somewhat unnecessary computational cost. Maybe change this later if explosions are too expensive?
            list.Clear();
            lookup.Tree.QueryAabb(ref list, (ref List<IEntity> list, in IEntity ent) =>
            {
                if (!ent.Deleted && !processed.Contains(ent.Uid) && !_containerSystem.IsEntityInContainer(ent.Uid, ent.Transform))
                    list.Add(ent);
                return true;
            }, gridBox);

            foreach (var ent in list)
            {
                ThrowEntity(ent, epicenter, intensity);
            }
        }

        /// <summary>
        ///     Same as <see cref="ExplodeTile"/>, but using a slower entity lookup and without tiles to damage.
        /// </summary>
        internal void ExplodeSpace(EntityLookupComponent lookup, IMapGrid grid, Vector2 tilePosLocal, float intensity, DamageSpecifier damage, MapCoordinates epicenter, HashSet<EntityUid> processed)
        {
            var worldBox = grid.WorldMatrix.TransformBox(Box2.UnitCentered.Translated(tilePosLocal));

            List<IEntity> list = new();
            lookup.Tree.QueryAabb(ref list, (ref List<IEntity> list, in IEntity ent) =>
            {
                if (!ent.Deleted &&
                    !processed.Contains(ent.Uid) &&
                    !_containerSystem.IsEntityInContainer(ent.Uid, ent.Transform))
                {
                    list.Add(ent);
                }
                return true;
            }, worldBox);

            // get entities on tile and store in array. Cannot use enumerator or we get fun errors.
            foreach (var entity in list)
            {
                if (entity.Transform.GridID != GridId.Invalid)
                    continue;

                _damageableSystem.TryChangeDamage(entity.Uid, damage);
            }

            list.Clear();
            lookup.Tree.QueryAabb(ref list, (ref List<IEntity> list, in IEntity ent) =>
            {
                if (!ent.Deleted &&
                    Box2.UnitCentered.Contains(grid.WorldToLocal(ent.Transform.WorldPosition) - tilePosLocal) &&
                    processed.Add(ent.Uid) &&
                     !_containerSystem.IsEntityInContainer(ent.Uid, ent.Transform))
                {
                    list.Add(ent);
                }
                return true;
            }, worldBox);

            foreach (var entity in list)
            {
                if (entity.Transform.GridID != GridId.Invalid)
                    continue;

                ThrowEntity(entity, epicenter, intensity);
            }
        }
    }

    class Explosion
    {
        /// <summary>
        ///     Used to avoid applying explosion effects repeatedly to the same entity.
        /// </summary>
        public readonly HashSet<EntityUid> ProcessedEntities = new();
        public int TileIteration = 1;

        public readonly MapCoordinates Epicenter;
        public readonly IMapGrid Grid;
        public readonly EntityUid MapUid;
        public readonly EntityLookupComponent MapLookup;
        public readonly EntityLookupComponent GridLookup;

        private readonly List<HashSet<Vector2i>> _tileSetList;
        private readonly List<float> _tileSetIntensity;
        private readonly DamageSpecifier _explosionDamage;
        private IEnumerator<Vector2i> _tileEnumerator;

        public Explosion(List<HashSet<Vector2i>> tileSetList, List<float> tileSetIntensity, IMapGrid grid, MapCoordinates epicenter, DamageSpecifier explosionDamage)
        {
            _tileSetList = tileSetList;
            _tileSetIntensity = tileSetIntensity;
            Epicenter = epicenter;
            Grid = grid;
            _explosionDamage = explosionDamage;

            // We will remove tile sets from the list as we process them. We want to start the explosion from the center (currently the first entry).
            // But this causes a slow List.RemoveAt(), reshuffling entries every time. So we reverse the list.
            _tileSetList.Reverse();
            _tileSetIntensity.Reverse();

            // Get the first tile enumerator set up
            _tileEnumerator = _tileSetList.Last().GetEnumerator();

            // Is there really no way to directly get the map uid?
            MapLookup = IoCManager.Resolve<IMapManager>().GetMapEntity(grid.ParentMapId).GetComponent<EntityLookupComponent>();
            GridLookup = IoCManager.Resolve<IEntityManager>().GetComponent<EntityLookupComponent>(grid.GridEntityId);
        }

        public IEnumerator<(Vector2i, float, DamageSpecifier)> GetEnumerator()
        {
            while (true)
            {
                // do we need to get the next tile index enumerator?
                if (!_tileEnumerator.MoveNext())
                {
                    TileIteration++;

                    if (_tileSetList.Count == 1)
                        break;

                    _tileSetList.RemoveAt(_tileSetList.Count - 1);
                    _tileSetIntensity.RemoveAt(_tileSetIntensity.Count - 1);
                    _tileEnumerator = _tileSetList[^1].GetEnumerator();
                    continue;
                }

                yield return (_tileEnumerator.Current, _tileSetIntensity[^1], _explosionDamage * _tileSetIntensity[^1]);
            }
        }
    }
}
