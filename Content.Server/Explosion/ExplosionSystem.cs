using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Camera;
using Content.Server.Explosion.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Throwing;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.Maps;
using Robust.Server.Containers;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Explosion
{
    // TODO:
    // - rejig the AirtightMap
    // - move functions from airtight to explosion
    // - Make tile break chance use the explosion prototype
    // - Improved directional blocking (if not become unblocked, only damage blocking entity)
    // - Add a vaporization threshold (damage dependent, so per-explosion type?)

    // TODO after opening draft:
    // - Make explosion window EUI & remove preview commands
    // - Grid jump

    public sealed partial class ExplosionSystem : EntitySystem
    {
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IRobustRandom _robustRandom = default!;
        [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly ContainerSystem _containerSystem = default!;
        [Dependency] private readonly NodeGroupSystem _nodeGroupSystem = default!;

        public int TilesPerTick { get; private set; }
        public bool PhysicsThrow { get; private set; }
        public bool SleepNodeSys { get; private set; }

        private int _previousTileIteration;

        /// <summary>
        ///     Queue for delayed processing of explosions.
        /// </summary>
        private Queue<Func<Explosion>> _explosionQueue = new();

        private Explosion? _activeExplosion;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<AirtightComponent, DamageChangedEvent>(OnAirtightDamaged);

            _cfg.OnValueChanged(CCVars.ExplosionTilesPerTick, value => TilesPerTick = value, true);
            _cfg.OnValueChanged(CCVars.ExplosionPhysicsThrow, value => PhysicsThrow = value, true);
            _cfg.OnValueChanged(CCVars.ExplosionSleepNodeSys, value => SleepNodeSys = value, true);
        }

        public override void Update(float frameTime)
        {
            if (_activeExplosion == null && _explosionQueue.Count == 0)
                // nothing to do
                return;

            var tilesToProcess = TilesPerTick;
            while (tilesToProcess > 0)
            {
                // if we don't have one, get a new explosion to process
                if (_activeExplosion == null)
                {
                    if (!_explosionQueue.TryDequeue(out var nextExplosion))
                        break;

                    _activeExplosion = nextExplosion();
                    _previousTileIteration = 0;

                    // just a lil nap
                    if (SleepNodeSys)
                        _nodeGroupSystem.Snoozing = true;
                }

                var processed = ProcessExplosion(_activeExplosion, tilesToProcess);
                tilesToProcess -= processed;
                if (processed == 0)
                    _activeExplosion = null;
            }

            if (_activeExplosion != null)
            {
                // update the client explosion overlays. This ensures that the fire-effects sync up with the entities currently being damaged.
                if (_previousTileIteration == _activeExplosion.TileIteration)
                    return;

                _previousTileIteration = _activeExplosion.TileIteration;
                RaiseNetworkEvent(new ExplosionOverlayUpdateEvent(_previousTileIteration));
                return;
            }

            // If we get here, we must have finished processing all explosions.

            // Clear client explosion overlays
            RaiseNetworkEvent(new ExplosionOverlayUpdateEvent(int.MaxValue));

            //wakey wakey
            _nodeGroupSystem.Snoozing = false;
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
                if (explosion.Grid.TryGetTileRef(tileIndices, out var tileRef))
                {
                    ExplodeTile(explosion.GridLookup, explosion.Grid, tileIndices, intensity, damage, explosion.Epicenter, explosion.ProcessedEntities);
                    DamageFloorTile(tileRef, intensity, damagedTiles);
                }

                ExplodeSpace(explosion.MapLookup, explosion.Grid, tileIndices, intensity, damage, explosion.Epicenter, explosion.ProcessedEntities);

                processedTiles++;
                if (processedTiles == tilesToProcess)
                    break;
            }


            explosion.Grid.SetTiles(damagedTiles);

            return processedTiles;
        }

        /// <summary>
        ///     Find the strength needed to generate an explosion of a given radius
        /// </summary>
        /// <remarks>
        ///     This assumes the explosion is in a vacuum / unobstructed. Given that explosions are not perfectly
        ///     circular, here radius actually means the sqrt(Area/pi), where the area is the total number of tiles
        ///     covered by the explosion. Until you get to radius 30+, this is functionally equivalent to the
        ///     actual radius.
        /// </remarks>
        public static float RadiusToIntensity(float radius, float slope, float maxIntensity = 0)
        {
            // If you consider the intensity at each tile in an explosion to be a height. Then a circular explosion is
            // shaped like a cone. So total intensity is like the volume of a cone with height = slope * radius. Of
            // course, as the explosions are not perfectly circular, this formula isn't perfect, but the formula works
            // reasonably well.

            var coneVolume = slope * MathF.PI / 3 * MathF.Pow(radius, 3);

            if (maxIntensity <= 0 || slope * radius < maxIntensity)
                return coneVolume;

            // This explosion is limited by the maxIntensity.
            // Instead of a cone, we have a conical frustum.

            // Subtract the volume of the missing cone segment, with height:
            var h =  slope * radius - maxIntensity;
            return coneVolume - (h * MathF.PI / 3 * MathF.Pow(h / slope, 2));
        }

        public void QueueExplosion(EntityUid uid, ExplosionPrototype type, float intensity, float slope, float maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            if (!EntityManager.TryGetComponent(uid, out ITransformComponent? transform))
                return;

            if (!_mapManager.TryGetGrid(transform.GridID, out var grid))
                return;

            QueueExplosion(transform.GridID, grid.TileIndicesFor(transform.Coordinates), type, intensity, slope, maxTileIntensity, excludedTiles);
        }

        public void QueueExplosion(GridId gridId, Vector2i epicenter, ExplosionPrototype type, float totalIntensity, float slope, float maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            if (totalIntensity <= 0 || slope <= 0)
                return;

            _explosionQueue.Enqueue(() => SpawnExplosion(gridId, epicenter, type, totalIntensity, slope, maxTileIntensity, excludedTiles));
        }

        private Explosion SpawnExplosion(GridId gridId, Vector2i epicenter, ExplosionPrototype type, float totalIntensity, float slope, float maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            var (tileSetList, tileSetIntensity) = GetExplosionTiles(gridId, epicenter, type.ID, totalIntensity, slope, maxTileIntensity, excludedTiles);
            var grid = _mapManager.GetGrid(gridId);

            RaiseNetworkEvent(new ExplosionEvent(grid.GridTileToWorld(epicenter), type.ID, tileSetList, tileSetIntensity, gridId));

            // sound & screen shake
            var explosionSoundParams = AudioParams.Default.WithAttenuation(Attenuation.InverseDistanceClamped);
            explosionSoundParams.RolloffFactor = 5f;
            explosionSoundParams.Volume = 0;
            var range = 3 * (tileSetList.Count - 2);
            var filter = Filter.Empty().AddInRange(grid.GridTileToWorld(epicenter), range);
            SoundSystem.Play(filter, type.Sound.GetSound(), explosionSoundParams.WithMaxDistance(range));
            CameraShakeInRange(filter, grid.GridTileToWorld(epicenter), totalIntensity);

            return new (type,
                        tileSetList,
                        tileSetIntensity!,
                        grid,
                        grid.GridTileToWorld(epicenter));
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
                var effect = (int) (5 * Math.Pow(totalIntensity,0.5) * (1 / (1 + distance)));
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

        /// <summary>
        ///     Find entities on a tile using GridTileLookupSystem and apply explosion effects. 
        /// </summary>
        public void ExplodeTile(EntityLookupComponent lookup, IMapGrid grid, Vector2i tile, float intensity, DamageSpecifier damage, MapCoordinates epicenter, HashSet<EntityUid> processed)
        {
            var gridBox = Box2.UnitCentered.Translated((Vector2) tile + 0.5f * grid.TileSize);

            var throwForce = 10 * MathF.Sqrt(intensity);

            void ProcessEntity(IEntity entity)
            {
                if (entity.Deleted || !processed.Add(entity.Uid) || _containerSystem.IsEntityInContainer(entity.Uid, entity.Transform))
                    return;

                _damageableSystem.TryChangeDamage(entity.Uid, damage);

                if (!PhysicsThrow || !entity.HasComponent<ExplosionLaunchedComponent>())
                    return;

                var location = entity.Transform.Coordinates.ToMap(EntityManager);

                entity.TryThrow(location.Position - epicenter.Position, throwForce);
            }

            List<IEntity> list = new();
            HashSet<EntityUid> set = new();
            lookup.Tree._b2Tree.FastQuery(ref gridBox, (ref IEntity entity) => list.Add(entity));
            
            foreach (var e in list)
            {
                set.Add(e.Uid);
                ProcessEntity(e);
            }

            foreach (var uid in grid.GetAnchoredEntities(tile).ToList())
            {
                _damageableSystem.TryChangeDamage(uid, damage);
            }

            // TODO EXPLOSIONS PERFORMANCE Here, we get the intersecting entities AGAIN for throwing. This way, glass
            // shards spawned from windows will be flung outwards, and not stay where they spawned. BUT this is also
            // somewhat unnecessary computational cost.
            if (!PhysicsThrow)
                return;

            list.Clear();
            lookup.Tree._b2Tree.FastQuery(ref gridBox, (ref IEntity entity) =>
            {
                if (!set.Contains(entity.Uid))
                    list.Add(entity);
            });

            foreach (var e in list)
                ProcessEntity(e);
        }

        /// <summary>
        ///     Same as <see cref="ExplodeTile"/>, but using a slower entity lookup and without tiles to damage.
        /// </summary>
        internal void ExplodeSpace(EntityLookupComponent lookup, IMapGrid grid, Vector2i tile, float intensity, DamageSpecifier damage, MapCoordinates epicenter, HashSet<EntityUid> processed)
        {
            var gridBox = Box2.UnitCentered.Translated((Vector2) tile + 0.5f * grid.TileSize);
            var worldBox = grid.WorldMatrix.TransformBox(gridBox);
            var throwForce = 10 * MathF.Sqrt(intensity);

            var matrix = grid.InvWorldMatrix;
            Func<IEntity, bool> contains = (IEntity entity) => gridBox.Contains(matrix.Transform(entity.Transform.WorldPosition));

            void ProcessEntity(IEntity entity)
            {
                if (entity.Deleted || !contains(entity) || !processed.Add(entity.Uid) || _containerSystem.IsEntityInContainer(entity.Uid, entity.Transform))
                    return;

                _damageableSystem.TryChangeDamage(entity.Uid, damage);

                if (!PhysicsThrow || !entity.HasComponent<ExplosionLaunchedComponent>())
                    return;

                var location = entity.Transform.Coordinates.ToMap(EntityManager);

                entity.TryThrow(location.Position - epicenter.Position, throwForce);
            }

            List<IEntity> list = new();
            HashSet<EntityUid> set = new();
            lookup.Tree._b2Tree.FastQuery(ref worldBox, (ref IEntity entity) => list.Add(entity));

            foreach (var e in list)
            {
                set.Add(e.Uid);
                ProcessEntity(e);
            }

            // TODO EXPLOSIONS PERFORMANCE Here, we get the intersecting entities AGAIN for throwing. This way, glass
            // shards spawned from windows will be flung outwards, and not stay where they spawned. BUT this is also
            // somewhat unnecessary computational cost. Maybe change this later if explosions are too expensive?
            if (!PhysicsThrow)
                return;

            list.Clear();
            lookup.Tree._b2Tree.FastQuery(ref worldBox, (ref IEntity entity) =>
            {
                if (!set.Contains(entity.Uid))
                    list.Add(entity);
            });

            foreach (var e in list)
                ProcessEntity(e);
        }
    }

    class Explosion
    {
        /// <summary>
        ///     Used to avoid applying explosion effects repeatedly to the same entity. Particularly important if the
        ///     explosion throws this entity, as then it will be moving while the explosion is happening.
        /// </summary>
        public readonly HashSet<EntityUid> ProcessedEntities = new();

        /// <summary>
        ///     Tracks how close this explosion is to having been fully processed. Used to update client side explosion overlays.
        /// </summary>
        public int TileIteration = 1;

        public readonly ExplosionPrototype ExplosionType;
        public readonly MapCoordinates Epicenter;
        public readonly IMapGrid Grid;
        public readonly EntityUid MapUid;
        public readonly EntityLookupComponent MapLookup;
        public readonly EntityLookupComponent GridLookup;

        private readonly List<HashSet<Vector2i>> _tileSetList;
        private readonly List<float> _tileSetIntensity;
        private IEnumerator<Vector2i> _tileEnumerator;

        public Explosion(ExplosionPrototype explosionType,
            List<HashSet<Vector2i>> tileSetList,
            List<float> tileSetIntensity,
            IMapGrid grid,
            MapCoordinates epicenter)
        {
            ExplosionType = explosionType;
            _tileSetList = tileSetList;
            _tileSetIntensity = tileSetIntensity;
            Epicenter = epicenter;
            Grid = grid;

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

                yield return (_tileEnumerator.Current, _tileSetIntensity[^1], ExplosionType.DamagePerIntensity * _tileSetIntensity[^1]);
            }
        }
    }
}
