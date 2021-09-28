using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Camera;
using Content.Server.Explosion.Components;
using Content.Server.Throwing;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.Maps;
using Content.Shared.Sound;
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
    // add comments:
    // capping intensity pancackes explosions
    // squashes the sandpile/cone down
    // --> more area


    // Explosion grid hop.
    // First note: IF an explosion is CONSTRAINED then it will deal more damage on average to the tiles it does have (AKA: it will have MORE ITERATIONS and a HIGHER MAX INTENSITTY).
    // Conversely, if an explosion is free, it will always deal less damage, have fewer iterations, and a lower max intensity.
    //
    // Given that during the initial explosion iteration, if the explosion spreads over anohter grid, it propagats FREELY, this means that it NECESSARILY will have fewer overall iterations than it would otherwise.
    // or put another way: IF we were to properly do a dynamic grid hop, unless that grid was COMPLETELY free of obstacles, it would result in fewer iterations.
    //
    // So: making a grid hop happen after the explosion has finishes propagating, and then just seeding a SECONDARY explosion, limited by the # of iterations rather than total strength, will ALWAYS deal less damaage over all.
    //
    // Is this a bad thing? I would argue no. A bit of sparation between the grids --> explosion leaks into spce--> less constrained --> less damage
    // it's realistic, so fuck it just do it like that

    // test that explosions are properly de-queued

    // todo if not fix at least figure out whyL some of the entities spawned by killing things DONT get thrown by secondary explosions
    // --> UNTILL I pick them up and drop them. are they considered children of the map or the entity that died?


    // todo make diagonal walls block explosions

    // todo:
    // atmos airtight instead of my thing
    // grid-jump
    // beter admin gui


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
        public static float RadiusToIntensity(float radius, float slope, float maxIntensity = 0)
        {
            // This formula came from fitting data, but if you want an intuitive explanation, then consider the
            // intensity of each tile in an explosion to be a height. Then a circular explosion is shaped like a cone.
            // So total intensity is like the volume of a cone with height = slope * radius. Of course, as the
            // explosions are not perfectly circular, this formula isn't perfect, but the formula works reasonably well.

            var coneVolume = (slope * MathF.PI / 3 * MathF.Pow(radius, 3));

            if (maxIntensity <= 0 || slope * radius < maxIntensity)
                return coneVolume;

            // This explosion is limited by the maxIntensity.
            // Instead of a cone, we have a conical frustum.

            // Subtract the volume of the missing cone segment, with height:
            var h =  slope * radius - maxIntensity;
            return coneVolume - (h * MathF.PI / 3 * MathF.Pow(h / slope, 2));
        }

        public void SpawnExplosion(IMapGrid grid, Vector2i epicenter, float intensity, float slope, int maxTileIntensity, HashSet<Vector2i>? excludedTiles = null)
        {
            var (tileSetList, tileSetIntensity) = GetExplosionTiles(grid, epicenter, intensity, slope, maxTileIntensity, excludedTiles);

            if (tileSetList == null)
                return;

            // Wow dem graphics
            RaiseNetworkEvent(new ExplosionEvent(grid.GridTileToWorld(epicenter), tileSetList, tileSetIntensity, grid.Index));

            // sound & screen shake
            var range = 3*(tileSetList.Count-2);
            var filter = Filter.Empty().AddInRange(grid.GridTileToWorld(epicenter), range);
            SoundSystem.Play(filter, _explosionSound.GetSound(), _explosionSoundParams.WithMaxDistance(range));
            CameraShakeInRange(filter, grid.GridTileToWorld(epicenter));

            _explosions.Enqueue(new Explosion(
                                    tileSetList,
                                    tileSetIntensity!,
                                    grid,
                                    grid.GridTileToWorld(epicenter),
                                    this,
                                    BaseExplosionDamage * slope));
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

                if (delta.EqualsApprox(Vector2.Zero))
                    delta = new(0.01f, 0);

                var distance = delta.Length;
                var effect = 10 * (1 / (1 + distance));
                if (effect > 0.01f)
                {
                    var kick = - delta.Normalized * effect;
                    recoil.Kick(kick);
                }
            }
        }

        /// <summary>
        ///     Decrease in intensity used for TileBreakChance calculation when repeatedly breaking a single tile.
        /// </summary>
        /// <remarks>
        ///     Effectively, in order for an explosion to have a chance of double-breaking a tile, the intensity needs
        ///     to be larger than 10. As tile breaking will eventually lead to space tiles & a vacuum forming, this
        ///     number should not be set too small. Otherwise even small explosions could punch a hole through the
        ///     station.
        /// </remarks>
        public const float TileBreakIntensityDecrease = 10f;

        /// <summary>
        ///     Explosion intensity dependent chance for a tile to break down to some base turf.
        /// </summary>
        private float TileBreakChance(float intensity)
        {
            // ~ 5% at intensity 2, ~ 80% at intensity 8. For intensity 10+, nearly 100%.
            // This means that with TileBreakIntensityDecrease = 10, intensity 12 -> ~5% chance of double break, 18 ->
            // ~80% chance of double break and so on.
            return (intensity < 1) ? 0 : (1 + MathF.Tanh(intensity/3 - 2)) / 2;
        }

        /// <summary>
        ///     Tries to damage the FLOOR TILE. Not to be confused with damaging / affecting entities intersecting the tile.
        /// </summary>
        private void DamageFloorTile(IMapGrid grid, Vector2i tileIndices, float intensity)
        {
            var tileRef = grid.GetTileRef(tileIndices);

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

            if (tileDef.TileId != tileRef.Tile.TypeId)
                grid.SetTile(tileIndices, new Tile(tileDef.TileId));
        }

        private void ThrowEntity(IEntity entity, MapCoordinates epicenter, float intensity)
        {
            if (!entity.HasComponent<ExplosionLaunchedComponent>())
                return;

            var targetLocation = entity.Transform.Coordinates.ToMap(EntityManager);
            var throwForce = 10 * MathF.Sqrt(intensity);

            entity.TryThrow(targetLocation.Position - epicenter.Position, throwForce);
        }

        public void ExplodeTile(Vector2i tile, IMapGrid grid, float intensity, DamageSpecifier damage, MapCoordinates epicenter, HashSet<EntityUid> ignored)
        {
            // get entities on tile and store in array. Cannot use enumerator or we get fun errors.
            foreach (var entity in _gridTileLookupSystem.GetEntitiesIntersecting(grid.Index, tile).ToArray())
            {
                // Entities in containers will be damaged if the container decides to pass the damage along. We
                // do not damage them directly. Similarly, we can just throw the container, not each individual entity.
                if (entity.Transform.ParentUid != grid.GridEntityId)
                    continue;

                // note, here we ONLY check if they are ignored. we add them in the second for loop, which MAY be removed eventually.
                if (ignored.Contains(entity.Uid))
                    continue;

                _damageableSystem.TryChangeDamage(entity.Uid, damage);
            }

            // TODO EXPLOSIONS PERFORMANCE
            // here, we get the intersecting entities AGAIN for throwing/
            // This way, glass shards spawned from windows ill be flung outwards, and not stay where they spawned.
            // BUT this is also somewhat unnecessary computational cost. Maybe change this later?
            foreach (var entity in _gridTileLookupSystem.GetEntitiesIntersecting(grid.Index, tile).ToArray())
            {
                if (entity.Transform.ParentUid != grid.GridEntityId)
                    continue;

                // note here we actually add and check if they are ignored
                if (!ignored.Add(entity.Uid))
                    continue;

                ThrowEntity(entity, epicenter, intensity);
            }

            // damage tile
            DamageFloorTile(grid, tile, intensity);
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
        private readonly DamageSpecifier _explosionDamage;
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
                    continue;
                }

                if (_grid.TryGetTileRef(_currentTileEnumerator.Current, out var tile))
                {

                }

                _system.ExplodeTile(_currentTileEnumerator.Current, _grid, _intensity, _intensity * _explosionDamage, _epicenter, _entities);
                processedTiles++;
            }

            return processedTiles;
        }
    }
}
