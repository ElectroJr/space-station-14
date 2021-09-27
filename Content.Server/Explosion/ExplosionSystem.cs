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
    //AFTER fixing the grid-lookup bug:
    // seperate the entity lookup for throwing and damage
    // add a todo making it clear that it is inefficient / a possible computational cost cutting measure
    // but at least it means that shards of glass will get flung outwards


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

            // Wow dem graphics
            RaiseNetworkEvent(new ExplosionEvent(tileSetList, tileSetIntensity, grid.Index));
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

        /// <summary>
        ///     Decrease in intensity used for TileBreakChance calculation when repeatedly breaking a single tile.
        /// </summary>
        /// <remarks>
        ///     Effectively, in order for an explosion to have a chance of double-breaking a tile, the intensity needs
        ///     to be larger than 10. As eventually, this will lead to space tiles & a vacuum forming, this should not
        ///     be set too small.
        /// </remarks>
        public const float TileBreakIntensityDecrease = 10f;

        /// <summary>
        ///     Explosion intensity dependent chance for a tile to break down to some base turf.
        /// </summary>
        private float TileBreakChance(float intensity)
        {
            // ~ 5% at intensity 2, 80% at intensity 8. For intensity 10+, nearly 100%.
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
            var direction = (targetLocation.Position - epicenter.Position).Normalized;
            var throwForce = 10 * MathF.Sqrt(intensity);

            entity.TryThrow(direction, throwForce);
        }

        public void ExplodeTile(Vector2i tile, IMapGrid grid, float intensity, DamageSpecifier damage, MapCoordinates epicenter, HashSet<EntityUid> ignored)
        {

            DamageFloorTile(grid, tile, intensity);


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

                _system.ExplodeTile(_currentTileEnumerator.Current, _grid, _intensity, _intensity * _explosionDamage, _epicenter, _entities);
                processedTiles++;
            }

            return processedTiles;
        }
    }
}
