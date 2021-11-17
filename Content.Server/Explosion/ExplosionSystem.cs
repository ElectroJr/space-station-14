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
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Explosion
{
    public sealed partial class ExplosionSystem : EntitySystem
    {
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IRobustRandom _robustRandom = default!;
        [Dependency] private readonly ITileDefinitionManager _tileDefinitionManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEntityLookup _entityLookup = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly ContainerSystem _containerSystem = default!;
        [Dependency] private readonly NodeGroupSystem _nodeGroupSystem = default!;

        /// <summary>
        ///     Queue for delayed processing of explosions. If there is an explosion that covers more than <see
        ///     cref="TilesPerTick"/> tiles, other explosions will actually be delayed slightly. Unless it's a station
        ///     nuke, this delay should never really be noticeable.
        /// </summary>
        private Queue<Func<Explosion>> _explosionQueue = new();

        /// <summary>
        ///     The explosion currently being processed.
        /// </summary>
        private Explosion? _activeExplosion;

        /// <summary>
        ///     How many tiles to "explode" per tick (deal damage, throw entities, break tiles).
        /// </summary>
        public int TilesPerTick { get; private set; }

        /// <summary>
        ///     Whether or not entities will be thrown by explosions. Turning this off helps a little bit with performance.
        /// </summary>
        public bool EnablePhysicsThrow { get; private set; }

        /// <summary>
        ///     Disables node group updating while the station is being shredded by an explosion.
        /// </summary>
        public bool SleepNodeSys { get; private set; }

        /// <summary>
        ///     While processing an explosion, the "progress" is sent to clients, so that the explosion fireball effect
        ///     syncs up with the damage. When the tile iteration increments, an update needs to be sent to clients.
        ///     This integer keeps track of the last value sent to clients.
        /// </summary>
        private int _previousTileIteration;

        private AudioParams _audioParams = AudioParams.Default
            .WithAttenuation(Attenuation.InverseDistanceClamped)
            .WithRolloffFactor(0.25f);

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<AirtightComponent, DamageChangedEvent>(OnAirtightDamaged);

            _cfg.OnValueChanged(CCVars.ExplosionTilesPerTick, value => TilesPerTick = value, true);
            _cfg.OnValueChanged(CCVars.ExplosionPhysicsThrow, value => EnablePhysicsThrow = value, true);
            _cfg.OnValueChanged(CCVars.ExplosionSleepNodeSys, value => SleepNodeSys = value, true);
        }

        /// <summary>
        ///     Process the explosion queue.
        /// </summary>
        public override void Update(float frameTime)
        {
            if (_activeExplosion == null && _explosionQueue.Count == 0)
                // nothing to do
                return;

            var tilesRemaining = TilesPerTick;
            while (tilesRemaining > 0)
            {
                // if there is no active explosion, get a new one to process
                if (_activeExplosion == null)
                {
                    if (!_explosionQueue.TryDequeue(out var spawnNextExplosion))
                        break;

                    _activeExplosion = spawnNextExplosion();
                    _previousTileIteration = 0;

                    // just a lil nap
                    if (SleepNodeSys)
                        _nodeGroupSystem.Snoozing = true;
                }

                var processed = ProcessExplosion(_activeExplosion, tilesRemaining);
                tilesRemaining -= processed;

                // has the explosion finished processing?
                if (_activeExplosion.FinishedProcessing)
                    _activeExplosion = null;
            }

            // we have finished processing our tiles. Is there still an ongoing explosion?
            if (_activeExplosion != null)
            {
                // update the client explosion overlays. This ensures that the fire-effects sync up with the entities currently being damaged.
                if (_previousTileIteration == _activeExplosion.CurrentIteration)
                    return;

                _previousTileIteration = _activeExplosion.CurrentIteration;
                RaiseNetworkEvent(new ExplosionOverlayUpdateEvent(_previousTileIteration));
                return;
            }

            // We have finished processing all explosions. Clear client explosion overlays
            RaiseNetworkEvent(new ExplosionOverlayUpdateEvent(int.MaxValue));

            //wakey wakey
            _nodeGroupSystem.Snoozing = false;
        }

        /// <summary>
        ///     Given an entity with an explosive component, spawn the appropriate explosion.
        /// </summary>
        /// <remarks>
        ///     Also accepts radius or intensity arguments. This is useful for explosives where the intensity is not
        ///     specified in the yaml / by the component, but determined dynamically (e.g., by the quantity of a
        ///     solution in a reaction).
        /// </remarks>
        public void TriggerExplosive(EntityUid uid, ExplosiveComponent? explosive = null, bool delete = true, float? totalIntensity = null, float? radius = null)
        {
            // log missing: false, because some entities (e.g. liquid tanks) attempt to trigger explosions when damaged,
            // but may not actually be explosive.
            if (!Resolve(uid, ref explosive, logMissing: false))
                return;

            // No reusable explosions here.
            if (explosive.Exploded)
                return;
            explosive.Exploded = true;

            // Override the explosion intensity if optional arguments were provided.
            if (radius != null)
                totalIntensity ??= RadiusToIntensity((float) radius, explosive.IntensitySlope, explosive.MaxIntensity);
            totalIntensity ??= explosive.TotalIntensity;

            QueueExplosion(uid,
                explosive.ExplosionType,
                (float) totalIntensity,
                explosive.IntensitySlope,
                explosive.MaxIntensity );

            if (delete)
                EntityManager.QueueDeleteEntity(uid);
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
        public float RadiusToIntensity(float radius, float slope, float maxIntensity = 0)
        {
            // If you consider the intensity at each tile in an explosion to be a height. Then a circular explosion is
            // shaped like a cone. So total intensity is like the volume of a cone with height = slope * radius. Of
            // course, as the explosions are not perfectly circular, this formula isn't perfect, but the formula works
            // reasonably well.

            // TODO EXPLOSION I guess this should actually use the formula for the volume of an Octagon-pyramid?

            var coneVolume = slope * MathF.PI / 3 * MathF.Pow(radius, 3);

            if (maxIntensity <= 0 || slope * radius < maxIntensity)
                return coneVolume;

            // This explosion is limited by the maxIntensity.
            // Instead of a cone, we have a conical frustum.

            // Subtract the volume of the missing cone segment, with height:
            var h =  slope * radius - maxIntensity;
            return coneVolume - h * MathF.PI / 3 * MathF.Pow(h / slope, 2);
        }

        #region Queueing
        /// <summary>
        ///     Queue an explosions, centered on some entity.
        /// </summary>
        public void QueueExplosion(EntityUid uid,
            string typeId,
            float intensity,
            float slope,
            float maxTileIntensity)
        {
            if (!EntityManager.TryGetComponent(uid, out TransformComponent? transform))
                return;

            QueueExplosion(transform.MapPosition, typeId, intensity, slope, maxTileIntensity);
        }

        /// <summary>
        ///     Queue an explosions, with a center specified by some map coordinates.
        /// </summary>
        public void QueueExplosion(MapCoordinates coords,
            string typeId,
            float intensity,
            float slope,
            float maxTileIntensity)
        {
            if (!_mapManager.TryFindGridAt(coords, out var grid))
            {
                // TODO EXPLOSIONS get proper multi-grid explosions working. For now, default to first grid.
                grid = _mapManager.GetAllMapGrids(coords.MapId).FirstOrDefault();
                if (grid == null)
                    return;
            }

            HashSet<Vector2i> initialTiles = new() { grid.TileIndicesFor(coords) };
            QueueExplosion(grid.Index, coords, initialTiles, typeId, intensity, slope, maxTileIntensity);
        }

        /// <summary>
        ///     Queue an explosion, with a specified epicenter and set of starting tiles.
        /// </summary>
        public void QueueExplosion(GridId gridId,
            MapCoordinates epicenter,
            HashSet<Vector2i> initialTiles,
            string typeId,
            float totalIntensity,
            float slope,
            float maxTileIntensity)
        {
            if (totalIntensity <= 0 || slope <= 0)
                return;

            if (!_prototypeManager.TryIndex<ExplosionPrototype>(typeId, out var type))
            {
                Logger.Error($"Attempted to spawn unknown explosion prototype: {type}");
                return;
            }

            if (!_mapManager.TryGetGrid(gridId, out var grid))
                return;

            _explosionQueue.Enqueue(() => SpawnExplosion(grid, epicenter, initialTiles, type, totalIntensity,
                slope, maxTileIntensity));
        }

        /// <summary>
        ///     This function actually spawns the explosion. It returns an <see cref="Explosion"/> instance with
        ///     information about the affected tiles for the explosion system to process. It will also trigger the
        ///     camera shake and sound effect.
        /// </summary>
        private Explosion SpawnExplosion(IMapGrid grid,
            MapCoordinates epicenter,
            HashSet<Vector2i> initialTiles,
            ExplosionPrototype type,
            float totalIntensity,
            float slope,
            float maxTileIntensity)
        {
            var (tileSetList, tileSetIntensity) = GetExplosionTiles(grid.Index, initialTiles, type.ID, totalIntensity,
                slope, maxTileIntensity);

            RaiseNetworkEvent(new ExplosionEvent(epicenter, type.ID, tileSetList, tileSetIntensity, grid.Index));

            // camera shake
            CameraShake(tileSetList.Count * 2.5f, epicenter, totalIntensity);

            // play sound. For whatever bloody reason, sound system requires ENTITY coordinates.
            var gridCoords = grid.MapToGrid(epicenter);
            var audioRange = tileSetList.Count * 5;
            var filter = Filter.Empty().AddInRange(epicenter, audioRange);
            SoundSystem.Play(filter, type.Sound.GetSound(), gridCoords, _audioParams.WithMaxDistance(audioRange));

            return new(type,
                        tileSetList,
                        tileSetIntensity!,
                        grid,
                        epicenter);
        }

        private void CameraShake(float range, MapCoordinates epicenter, float totalIntensity)
        {
            foreach (var player in _playerManager.GetPlayersInRange(epicenter, (int) range))
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
                var effect = 5 * MathF.Pow(totalIntensity, 0.5f) * (1 - distance / range);
                if (effect > 0.01f)
                    recoil.Kick(-delta.Normalized * effect);
            }
        }
        #endregion

        #region Processing
        /// <summary>
        ///     Deal damage, throw entities, and break tiles. Returns the number tiles that were processed.
        /// </summary>
        private int ProcessExplosion(Explosion explosion, int tilesToProcess)
        {
            var processedTiles = 0;
            List<(Vector2i, Tile)> damagedTiles = new();

            var mapUid = _mapManager.GetMapEntityId(explosion.Grid.ParentMapId);
            if (!mapUid.IsValid() || !EntityManager.TryGetComponent(mapUid, out EntityLookupComponent mapLookup) ||
                !EntityManager.TryGetComponent(explosion.Grid.GridEntityId, out EntityLookupComponent gridLookup))
            {
                // This should never happen. But Content integration tests are a magical realm where apparently anything goes.
                explosion.FinishedProcessing = true;
                return 0;
            }

            foreach (var (tileIndices, intensity, damage) in explosion)
            {
                if (explosion.Grid.TryGetTileRef(tileIndices, out var tileRef) && !tileRef.Tile.IsEmpty)
                {
                    ExplodeTile(gridLookup,
                        explosion.Grid,
                        tileIndices,
                        intensity,
                        damage,
                        explosion.Epicenter,
                        explosion.ProcessedEntities);

                    DamageFloorTile(tileRef, intensity, damagedTiles, explosion.ExplosionType);
                }
                else
                    ExplodeSpace(mapLookup,
                        explosion.Grid,
                        tileIndices,
                        intensity,
                        damage,
                        explosion.Epicenter,
                        explosion.ProcessedEntities);

                processedTiles++;
                if (processedTiles == tilesToProcess)
                    break;
            }

            explosion.Grid.SetTiles(damagedTiles);

            return processedTiles;
        }

        /// <summary>
        ///     Find entities on a grid tile using the EntityLookupComponent and apply explosion effects. 
        /// </summary>
        public void ExplodeTile(EntityLookupComponent lookup,
            IMapGrid grid,
            Vector2i tile,
            float intensity,
            DamageSpecifier damage,
            MapCoordinates epicenter,
            HashSet<EntityUid> processed)
        {
            var gridBox = Box2.UnitCentered.Translated((Vector2) tile + 0.5f * grid.TileSize);
            var throwForce = 10 * MathF.Sqrt(intensity);

            // get the entities on a tile. Note that we cannot process them directly, or we get
            // enumerator-changed-while-enumerating errors.
            List<EntityUid> list = new();
            _entityLookup.FastEntitiesIntersecting(lookup, ref gridBox, entity => list.Add(entity.Uid));
            list.AddRange(grid.GetAnchoredEntities(tile));

            // process those entities
            foreach (var entity in list)
            {
                ProcessEntity(entity, epicenter, processed, damage, throwForce);
            }

            // Next, we get the intersecting entities AGAIN, but purely for throwing. This way, glass shards spawned
            // from windows will be flung outwards, and not stay where they spawned. This is however somewhat
            // unnecessary, and a prime candidate for computational cost-cutting
            // TODO EXPLOSIONS PERFORMANCE keep this?
            if (!EnablePhysicsThrow)
                return;

            list.Clear();
            _entityLookup.FastEntitiesIntersecting(lookup, ref gridBox, entity => list.Add(entity.Uid));

            foreach (var e in list)
            {
                // Here we only throw, no dealing damage. Containers n such might drop their entities after being destroyed, but
                // they handle their own damage pass-through.
                ProcessEntity(e, epicenter, processed, null, throwForce);
            }
        }

        /// <summary>
        ///     Same as <see cref="ExplodeTile"/>, but for SPAAAAAAACE.
        /// </summary>
        internal void ExplodeSpace(EntityLookupComponent lookup,
            IMapGrid grid,
            Vector2i tile,
            float intensity,
            DamageSpecifier damage,
            MapCoordinates epicenter,
            HashSet<EntityUid> processed)
        {
            var gridBox = Box2.UnitCentered.Translated((Vector2) tile + 0.5f * grid.TileSize);
            var throwForce = 10 * MathF.Sqrt(intensity);
            var worldBox = grid.WorldMatrix.TransformBox(gridBox);
            var matrix = grid.InvWorldMatrix;
            List<EntityUid> list = new();

            EntityQueryCallback callback = (entity) =>
            {
                if (gridBox.Contains(matrix.Transform(entity.Transform.WorldPosition)))
                    list.Add(entity.Uid);
            };

            _entityLookup.FastEntitiesIntersecting(lookup, ref worldBox, callback);

            foreach (var entity in list)
            {
                ProcessEntity(entity, epicenter, processed, damage, throwForce);
            }

            if (!EnablePhysicsThrow)
                return;

            list.Clear();
            _entityLookup.FastEntitiesIntersecting(lookup, ref worldBox, callback);
            foreach (var entity in list)
            {
                ProcessEntity(entity, epicenter, processed, null, throwForce);
            }
        }

        /// <summary>
        ///     This function actually applies the explosion affects to an entity.
        /// </summary>
        private void ProcessEntity(EntityUid uid, MapCoordinates epicenter, HashSet<EntityUid> processed,
            DamageSpecifier? damage = null, float? throwForce = null)
        {
            // check whether this is a valid target, and whether we have already damaged this entity (can happen with
            // explosion-throwing).
            if (_containerSystem.IsEntityInContainer(uid) || !processed.Add(uid))
                return;

            // damage
            if (damage != null)
                _damageableSystem.TryChangeDamage(uid, damage);

            // throw
            if (throwForce != null && EnablePhysicsThrow &&
                EntityManager.HasComponent<ExplosionLaunchedComponent>(uid) &&
                EntityManager.TryGetComponent(uid, out TransformComponent transform))
            {
                EntityManager.GetEntity(uid).TryThrow(transform.WorldPosition - epicenter.Position, throwForce.Value);
            }

            // TODO EXPLOSION puddle / flammable ignite?

            // TODO EXPLOSION deaf/ear damage? other explosion effects?
        }

        /// <summary>
        ///     Tries to damage floor tiles. Not to be confused with the function that damages entities intersecting the
        ///     grid tile.
        /// </summary>
        public void DamageFloorTile(TileRef tileRef,
            float intensity,
            List<(Vector2i GridIndices, Tile Tile)> damagedTiles,
            ExplosionPrototype type)
        {
            if (tileRef.Tile.IsEmpty || tileRef.IsBlockedTurf(false))
                return;

            var tileDef = _tileDefinitionManager[tileRef.Tile.TypeId];

            while (_robustRandom.Prob(type.TileBreakChance(intensity)))
            {
                intensity -= type.TileBreakRerollReduction;

                if (tileDef is not ContentTileDefinition contentTileDef)
                    break;

                // does this have a base-turf that we can break it down to?
                if (contentTileDef.BaseTurfs.Count == 0)
                    break;

                tileDef = _tileDefinitionManager[contentTileDef.BaseTurfs[^1]];
            }

            if (tileDef.TileId == tileRef.Tile.TypeId)
                return;

            damagedTiles.Add((tileRef.GridIndices, new Tile(tileDef.TileId)));
        }
        #endregion
    }

    /// <summary>
    ///     This is a data class that stores information about the area affected by an explosion, for processing by <see
    ///     cref="ExplosionSystem"/>.
    /// </summary>
    class Explosion
    {
        /// <summary>
        ///     Used to avoid applying explosion effects repeatedly to the same entity. Particularly important if the
        ///     explosion throws this entity, as then it will be moving while the explosion is happening.
        /// </summary>
        public readonly HashSet<EntityUid> ProcessedEntities = new();

        /// <summary>
        ///     This integer tracks how much of this explosion has been processed.
        /// </summary>
        public int CurrentIteration = 0;

        public readonly ExplosionPrototype ExplosionType;
        public readonly MapCoordinates Epicenter;
        public readonly IMapGrid Grid;
        public readonly EntityUid MapUid;
        public bool FinishedProcessing;

        private readonly List<HashSet<Vector2i>> _tileSetList;
        private readonly List<float> _tileSetIntensity;

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
        }

        public IEnumerator<(Vector2i, float, DamageSpecifier)> GetEnumerator()
        {
            // We're not just using a foreach loop as we need to keep track of the CurrentIteration 
            while (CurrentIteration < _tileSetList.Count)
            {
                var tileEnumerator = _tileSetList[CurrentIteration].GetEnumerator();

                while (tileEnumerator.MoveNext())
                {
                    yield return (tileEnumerator.Current,
                    _tileSetIntensity[CurrentIteration],
                    ExplosionType.DamagePerIntensity * _tileSetIntensity[CurrentIteration]);
                }

                CurrentIteration++;
            }

            FinishedProcessing = true;
        }
    }
}
