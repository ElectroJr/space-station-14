using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Camera;
using Content.Server.Explosion.Components;
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

        /// <summary>
        ///     Queue for delayed processing of explosions.
        /// </summary>
        private Queue<Explosion> _explosions = new();

        /// <summary>
        ///     Max # of entities to damage every update. At least one set is processed every tick.
        /// </summary>
        public int MaxEntitities = 20;

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

            int totalProcessed = 0;
            while (_explosions.TryDequeue(out var tuple))
            {
                DamageEntities(tuple.Item1, tuple.Item2);
                ThrowEntities(tuple.Item1, tuple.Item2);

                totalProcessed += tuple.Item1.Count;

                if (_explosions.TryPeek(out var next) && totalProcessed + next.Item1.Count > MaxEntitities)
                    break;
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

        /// <summary>
        ///     Given a list of tile-sets, get the entities that occupy those tiles.
        /// </summary>
        /// <remarks>
        ///     This is used to map the explosion-intensity-tiles to entities that need to be damaged.
        /// </remarks>
        public List<EntityUid> GetEntities(List<HashSet<Vector2i>> tileSetList, IMapGrid grid)
        {
            List<List<EntityUid>> result = new();

            // Some entities straddle more than one tile. We do not want to add them twice. The damage they take will
            // depend on the highest-intensity tile they intersect.
            HashSet<EntityUid> known = new();

            // Here we iterate over tile sets. In a circular explosion, each tile set is a ring of constant distance.
            foreach (var tileSet in tileSetList)
            {
                result.Add(new());
                foreach (var tile in tileSet)
                {
                    // For each tile in this ring, we need to find the intersecting entities. Fortunately
                    // _gridTileLookupSystem is pretty fast.
                    foreach (var entity in _gridTileLookupSystem.GetEntitiesIntersecting(grid.Index, tile))
                    {
                        // Entities in containers will be damaged if the container decides to pass the damage along. We
                        // do not damage them directly.
                        if (entity.Transform.ParentUid != grid.GridEntityId)
                            continue;

                        // Did we already add this entity?
                        if (known.Contains(entity.Uid))
                            continue;

                        result.Last().Add(entity.Uid);
                        known.Add(entity.Uid);
                    }
                }
            }

            return result;
        }

        public void SpawnExplosion(IMapGrid grid, Vector2i epicenter, int intensity, int damageScale, int maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            var (tileSetList, tileSetIntensity) = GetExplosionTiles(grid, epicenter, intensity, damageScale, maxTileIntensity, excludedTiles);

            if (tileSetList == null)
                return;



            // sound & screen shake
            var range = 5*MathF.Max(IntensityToRadius(intensity), (tileSetList.Count-2) * grid.TileSize);
            var filter = Filter.Empty().AddInRange(grid.GridTileToWorld(epicenter), range);
            SoundSystem.Play(filter, _explosionSound.GetSound(), _explosionSoundParams.WithMaxDistance(range));
            CameraShakeInRange(filter, grid.GridTileToWorld(epicenter));
        }

        public void DamageEntities(List<EntityUid> entities, float scale)
        {
            var damage = BaseExplosionDamage * scale;
            foreach (var entity in entities)
            {
                _damageableSystem.TryChangeDamage(entity, damage);
            }
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

        private void ThrowEntities(List<EntityUid> entities, float force)
        {
            foreach (var entity in entities)
            {
                if (!ComponentManager.HasComponent<ExplosionLaunchedComponent>(entity))
                    continue;

            }

            var sourceLocation = eventArgs.Source;
            var targetLocation = eventArgs.Target.Transform.Coordinates;

            if (sourceLocation.Equals(targetLocation)) return;

            var direction = (targetLocation.ToMapPos(Owner.EntityManager) - sourceLocation.ToMapPos(Owner.EntityManager)).Normalized;

            var throwForce = eventArgs.Severity switch
            {
                ExplosionSeverity.Heavy => 30,
                ExplosionSeverity.Light => 20,
                _ => 0,
            };

            Owner.TryThrow(direction, throwForce);
        }


        class Explosion
        {
            public HashSet<EntityUid> Entities = new();
            public List<HashSet<Vector2i>> TileSetList;
            public List<float> TileSetIntensity;
            public EntityCoordinates Epicenter;

            private int _processSubIndex = 0;
            private int _processIndex = 1;

            /// <summary>
            ///     Deal damage, throw entities, and break tiles.
            /// </summary>
            private bool Process()
            {
                if (TileSetList.Count == _processIndex)
                    // we are done processing
                    return true;

                var tileset = TileSetList[_processIndex];

                while (_processSubIndex < tileset.Count)
                {
                    GetEntities(tileSetList, grid)
                }
                foreach (var entitySet in GetEntities(tileSetList, grid))
                {
                    
                }


                if (explosion.Entities == null)
                {

                }

                _processIndex++;
                _processSubIndex = 0;

                return (TileSetList.Count == _processIndex);
            }

        }
    }



}
