using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Camera;
using Content.Server.Explosion.Components;
using Content.Server.Throwing;
using Content.Shared.Damage;
using Content.Shared.Sound;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.Server.Explosion
{
    // test that explosions are properly de-queued

    // todo if not fix at least figure out whyL some of the entities spawned by killing things DONT get thrown by secondary explosions
    // --> UNTILL I pick them up and drop them. are they considered children of the map or the entity that died?


    //todo make diagonal walls block explosions

    // todo:
    // atmos airtight instead of my thing
    // grid-jump
    // launch direction is gonna be hard



    // damage values:
    // Light => 20 --> intensity = 2
    // Heavy => 60 -> intensity = 4
    // Destruction -> 250, 15*15 = 225, so severity ~ 15


    // Todo create explosion prototypes.
    // E.g. Fireball (heat), AP (heat+piercing), HE (heat+blunt), dirty (heat+radiation)
    // Each explosion type will need it's own threshold map

    public sealed partial class ExplosionSystem : EntitySystem
    {
        private SoundSpecifier _explosionSound = default!;
        private AudioParams _explosionSoundParams = default!;

        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly ExplosionBlockerSystem _explosionBlockerSystem = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly GridTileLookupSystem _gridTileLookupSystem = default!;

        public const int MaxTilesPerTick = 20;

        /// <summary>
        ///     Queue for delayed processing of explosions.
        /// </summary>
        private Queue<Explosion> _explosions = new();

        public DamageSpecifier BaseExplosionDamage = new();

        public override void Initialize()
        {
            base.Initialize();

            _explosionSoundParams  = AudioParams.Default.WithAttenuation(Attenuation.InverseDistanceClamped);
            _explosionSoundParams.RolloffFactor = 10f; // Why does attenuation not work??
            _explosionSoundParams.Volume = -10;

            // TODO EXPLOSIONS change volume based on intensity? Given that explosions deal damage iteratively, maybe
            // also match sound duration or modulate the sound as the explosion progresses?

            // TODO YAML prototypes
            _explosionSound = new SoundCollectionSpecifier("explosion");
            BaseExplosionDamage.DamageDict = new() { { "Heat", 5 }, { "Blunt", 5 }, { "Piercing", 5 } };
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            int tilesToProcess = MaxTilesPerTick;
            while (tilesToProcess > 0 && _explosions.TryPeek(out var explosion))
            {
                var processed = explosion.Process(tilesToProcess);
                tilesToProcess -= processed;

                if (processed == 0)
                    _explosions.Dequeue();
            }
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
        public int RadiusToIntensity(float radius)
        {
            // This formula came from fitting data, but if you want an intuitive explanation, then consider the
            // intensity of each tile in an explosion to be a height. Then a circular explosion is shaped like a cone.
            // So total intensity is like the volume of a cone with height = 2 * radius

            // Of course, as the explosions are not perfectly circular, this formula isn't perfect. But the formula
            // works **really** well. The error stays below 1 tile up until a radius of 30, and only goes up to 1.4 with
            // a radius of ~60.

            return (int) ( 2 * MathF.PI / 3  * MathF.Pow(radius, 3));
        }

        /// <summary>
        ///     The inverse of <see cref="RadiusToIntensity(float)"/>
        /// </summary>
        public float IntensityToRadius(float intensity) => MathF.Cbrt(intensity) / (2 * MathF.PI / 3);

        public void SpawnExplosion(IMapGrid grid, Vector2i epicenter, int intensity, int damageScale, int maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            var (tileSetList, tileSetIntensity) = GetExplosionTiles(grid, epicenter, intensity, damageScale, maxTileIntensity, excludedTiles);

            if (tileSetList == null)
                return;

            _explosions.Enqueue(new Explosion(
                                    tileSetList,
                                    tileSetIntensity!,
                                    grid,
                                    grid.GridTileToWorld(epicenter),
                                    this,
                                    BaseExplosionDamage * damageScale) );

            // sound & screen shake
            var range = 5*MathF.Max(IntensityToRadius(intensity), (tileSetList.Count-2) * grid.TileSize);
            var filter = Filter.Empty().AddInRange(grid.GridTileToWorld(epicenter), range);
            SoundSystem.Play(filter, _explosionSound.GetSound(), _explosionSoundParams.WithMaxDistance(range));
            CameraShakeInRange(filter, grid.GridTileToWorld(epicenter));
        }

        private void CameraShakeInRange(Filter filter, MapCoordinates epicenter)
        {
            foreach (var player in filter.Recipients)
            {
                if (player.AttachedEntity == null)
                    continue;

                if (!ComponentManager.TryGetComponent(player.AttachedEntity.Uid, out CameraRecoilComponent? recoil))
                    continue;

                var playerPos = player.AttachedEntity.Transform.WorldPosition;
                var delta = epicenter.Position - playerPos;

                var distance = delta.Length;
                var effect = 10 * (1 / (1 + distance));
                if (effect > 0.01f)
                {
                    var kick = - delta.Normalized * effect;
                    recoil.Kick(kick);
                }
            }
        }

        private void ThrowEntity(IEntity entity, MapCoordinates epicenter, float intensity)
        {
            if (!entity.HasComponent<ExplosionLaunchedComponent>())
                return;

            var targetLocation = entity.Transform.Coordinates.ToMap(EntityManager);
            var direction = (targetLocation.Position - epicenter.Position).Normalized;
            var throwForce = 10 * MathF.Sqrt(intensity);

            entity.TryThrow(direction, throwForce);
        }

        public void ExplodeTile(Vector2i tile, IMapGrid grid, float intensity, DamageSpecifier damage, MapCoordinates epicenter, HashSet<EntityUid> ignored)
        {
            // get entities on tile and store in array. Cannot use enumerator or we get fun errors.
            var entities = _gridTileLookupSystem.GetEntitiesIntersecting(grid.Index, tile).ToArray();

            foreach (var entity in entities)
            {
                // Entities in containers will be damaged if the container decides to pass the damage along. We
                // do not damage them directly. Similarly, we can just throw the container, not each individual entity.
                if (entity.Transform.ParentUid != grid.GridEntityId)
                    continue;

                if (!ignored.Add(entity.Uid))
                    continue;

                _damageableSystem.TryChangeDamage(entity.Uid, damage);
                ThrowEntity(entity, epicenter, intensity);
            }

            // damage tile
        }
    }

    class Explosion
    {
        private readonly HashSet<EntityUid> _entities = new();
        private readonly List<HashSet<Vector2i>> _tileSetList;
        private readonly List<float> _tileSetIntensity;
        private readonly MapCoordinates _epicenter;
        private readonly ExplosionSystem _system;
        private readonly IMapGrid _grid;
        private DamageSpecifier _explosionDamage;
        private IEnumerator<Vector2i> _currentTileEnumerator;
        private float _intensity;

        public Explosion(List<HashSet<Vector2i>> tileSetList, List<float> tileSetIntensity, IMapGrid grid, MapCoordinates epicenter, ExplosionSystem system, DamageSpecifier explosionDamage)
        {
            _tileSetList = tileSetList;
            _tileSetIntensity = tileSetIntensity;
            _epicenter = epicenter;
            _system = system;
            _grid = grid;
            _explosionDamage = explosionDamage;

            // We will delete tile sets as we process, starting from what was the first element. So reverse order for faster List.RemoveAt();
            _tileSetList.Reverse();
            _tileSetIntensity.Reverse();

            // Get the first tile enumerator set up
            _currentTileEnumerator = _tileSetList.Last().GetEnumerator();
            _intensity = _tileSetIntensity.Last();
            _tileSetList.RemoveAt(_tileSetList.Count - 1);
            _tileSetIntensity.RemoveAt(_tileSetIntensity.Count - 1);
        }

        /// <summary>
        ///     Deal damage, throw entities, and break tiles. Returns the number tiles that were processed.
        /// </summary>
        public int Process(int tilesToProcess)
        {
            var processedTiles = 0;

            while (processedTiles < tilesToProcess)
            {
                // do we need to get the next enumerator?
                if (!_currentTileEnumerator.MoveNext())
                {
                    // are there any more left?
                    if (_tileSetList.Count == 0)
                        break;

                    _currentTileEnumerator = _tileSetList.Last().GetEnumerator();
                    _intensity = _tileSetIntensity.Last();
                    _tileSetList.RemoveAt(_tileSetList.Count - 1);
                    _tileSetIntensity.RemoveAt(_tileSetIntensity.Count - 1);
                }

                _system.ExplodeTile(_currentTileEnumerator.Current, _grid, _intensity, _intensity*_explosionDamage, _epicenter, _entities);
                processedTiles++;
            }

            return processedTiles;
        }
    }
}
