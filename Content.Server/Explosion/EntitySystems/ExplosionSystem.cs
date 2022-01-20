using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server.Atmos.Components;
using Content.Server.Explosion.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Throwing;
using Content.Shared.Camera;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Robust.Server.Containers;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Explosion.EntitySystems;

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
    [Dependency] private readonly CameraRecoilSystem _recoilSystem = default!;

    /// <summary>
    ///     Used to identify explosions when communicating with the client. Might be needed if more than one explosion is spawned in a single tick.
    /// </summary>
    /// <remarks>
    ///     Overflowing back to 0 should cause no issue, as long as you don't have more than 256 explosions happening in a single tick.
    /// </remarks>
    private byte _explosionCounter = 0;
    // maybe should just use a UID/explosion-entity and a state to convey information?
    // but then need to ignore PVS?

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

    private AudioParams _audioParams = AudioParams.Default.WithVolume(-5f);

    public override void Initialize()
    {
        base.Initialize();

        // handled in ExplosionSystemGridMap.cs
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<GridStartupEvent>(OnGridStartup);
        SubscribeLocalEvent<ExplosionResistanceComponent, GetExplosionResistanceEvent>(OnGetResistance);
        _mapManager.TileChanged += MapManagerOnTileChanged;

        // handled in ExplosionSystemAirtight.cs
        SubscribeLocalEvent<AirtightComponent, DamageChangedEvent>(OnAirtightDamaged);

        _cfg.OnValueChanged(CCVars.ExplosionTilesPerTick, value => TilesPerTick = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionPhysicsThrow, value => EnablePhysicsThrow = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionSleepNodeSys, value => SleepNodeSys = value, true);
    }
    public override void Shutdown()
    {
        base.Shutdown();
        _mapManager.TileChanged -= MapManagerOnTileChanged;
    }

    private void OnGetResistance(EntityUid uid, ExplosionResistanceComponent component, GetExplosionResistanceEvent args)
    {
        args.Resistance += component.GlobalResistance;
        if (component.Resistances.TryGetValue(args.ExplotionPrototype, out var resistance))
            args.Resistance += resistance;
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

                _explosionCounter++;
                _activeExplosion = spawnNextExplosion();
                _previousTileIteration = 0;

                // just a lil nap
                if (SleepNodeSys)
                    _nodeGroupSystem.Snoozing = true;
            }

            var processed = _activeExplosion.Proccess(tilesRemaining);
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
            RaiseNetworkEvent(new ExplosionOverlayUpdateEvent(_explosionCounter, _previousTileIteration + 1));
            return;
        }

        // We have finished processing all explosions. Clear client explosion overlays
        RaiseNetworkEvent(new ExplosionOverlayUpdateEvent(_explosionCounter, int.MaxValue));

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
            explosive.MaxIntensity);

        if (delete)
            EntityManager.QueueDeleteEntity(uid);
    }

    /// <summary>
    ///     Find the strength needed to generate an explosion of a given radius. More useful for radii larger then 4, when the explosion becomes less "blocky".
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

        // TODO EXPLOSION I guess this should actually use the formula for the volume of a distorted octagonal frustum?

        var coneVolume = slope * MathF.PI / 3 * MathF.Pow(radius, 3);

        if (maxIntensity <= 0 || slope * radius < maxIntensity)
            return coneVolume;

        // This explosion is limited by the maxIntensity.
        // Instead of a cone, we have a conical frustum.

        // Subtract the volume of the missing cone segment, with height:
        var h = slope * radius - maxIntensity;
        return coneVolume - h * MathF.PI / 3 * MathF.Pow(h / slope, 2);
    }

    // inverse of RadiusToIntensity, if you neglect maxIntensity.
    // only needed for getting nearby grids, so good enough for me.
    public float ApproxIntensityToRadius(float totalIntensity, float slope)
    {
        return MathF.Cbrt(3 * totalIntensity / (slope * MathF.PI));
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
    ///     Queue an explosion, with a specified epicenter and set of starting tiles.
    /// </summary>
    public void QueueExplosion(MapCoordinates epicenter,
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

        _explosionQueue.Enqueue(() => SpawnExplosion(epicenter, type, totalIntensity,
            slope, maxTileIntensity));
    }

    /// <summary>
    ///     This function actually spawns the explosion. It returns an <see cref="Explosion"/> instance with
    ///     information about the affected tiles for the explosion system to process. It will also trigger the
    ///     camera shake and sound effect.
    /// </summary>
    private Explosion SpawnExplosion(MapCoordinates epicenter,
        ExplosionPrototype type,
        float totalIntensity,
        float slope,
        float maxTileIntensity)
    {
        Vector2i initialTile;
        GridId gridId;
        var refGridId = GetReferenceGrid(epicenter, totalIntensity, slope);

        if (_mapManager.TryFindGridAt(epicenter, out var grid) &&
            grid.TryGetTileRef(grid.WorldToTile(epicenter.Position), out var tileRef) &&
            !tileRef.Tile.IsEmpty)
        {
            gridId = grid.Index;
            initialTile = tileRef.GridIndices;
        }
        else
        {
            gridId = GridId.Invalid; // implies space

            if (refGridId.IsValid())
            {
                initialTile = _mapManager.GetGrid(refGridId).WorldToTile(epicenter.Position);
            }
            else
            {
                initialTile = new Vector2i(
                    (int) Math.Floor(epicenter.Position.X / DefaultTileSize),
                    (int) Math.Floor(epicenter.Position.Y / DefaultTileSize));
            }
        }
        
        var (tileSetIntensity, spaceData, gridData) = GetExplosionTiles(epicenter.MapId, gridId, initialTile, refGridId, type.ID, totalIntensity, slope, maxTileIntensity);

        RaiseNetworkEvent(GetExplosionEvent(epicenter, type.ID, spaceData, gridData.Values, tileSetIntensity));

        // camera shake
        CameraShake(tileSetIntensity.Count * 2.5f, epicenter, totalIntensity);

        //For whatever bloody reason, sound system requires ENTITY coordinates.
        var mapEntityCoords = EntityCoordinates.FromMap(EntityManager, _mapManager.GetMapEntityId(epicenter.MapId), epicenter);

        // play sound. 
        var audioRange = tileSetIntensity.Count * 5;
        var filter = Filter.Pvs(epicenter).AddInRange(epicenter, audioRange);
        SoundSystem.Play(filter, type.Sound.GetSound(), mapEntityCoords, _audioParams);

        return new(this,
            type,
            spaceData,
            gridData.Values.ToList(),
            tileSetIntensity,
            epicenter);
    }

    /// <summary>
    ///     Look for grids in an area and select the heaviest one to orient an explosion in space.
    /// </summary>
    public GridId GetReferenceGrid(MapCoordinates epicenter, float totalIntensity, float slope)
    {
        var diameter = 2 * ApproxIntensityToRadius(totalIntensity, slope);

        GridId result = GridId.Invalid;
        float mass = 0;

        var grids = _mapManager.FindGridsIntersecting(epicenter.MapId, Box2.CenteredAround(epicenter.Position, (diameter, diameter)));
        foreach (var grid in grids)
        {
            if (TryComp(grid.GridEntityId, out PhysicsComponent? physics) && physics.Mass > mass)
            {
                mass = physics.Mass;
                result = grid.Index;
            }
        }

        return result;
    }

    public ExplosionEvent GetExplosionEvent(MapCoordinates epicenter, string id, SpaceExplosion? spaceData, IEnumerable<GridExplosion> gridData, List<float> tileSetIntensity)
    {
        Dictionary<GridId, Dictionary<int, HashSet<Vector2i>>> tiles = new();

        var spaceMatrix = Matrix3.Identity;

        if (spaceData != null)
        {
            spaceMatrix = spaceData.Matrix;
            tiles.Add(GridId.Invalid, spaceData.TileSets);
        }

        foreach (var grid in gridData)
        {
            tiles.Add(grid.GridId, grid.TileSets);
        }

        return new ExplosionEvent(_explosionCounter, epicenter, id, tileSetIntensity, tiles, spaceMatrix);
    }

    private void CameraShake(float range, MapCoordinates epicenter, float totalIntensity)
    {
        var players = Filter.Empty();
        players.AddInRange(epicenter, range, _playerManager, EntityManager);

        foreach (var player in players.Recipients)
        {
            if (player.AttachedEntity is not EntityUid uid)
                continue;

            var playerPos = Transform(player.AttachedEntity!.Value).WorldPosition;
            var delta = epicenter.Position - playerPos;

            if (delta.EqualsApprox(Vector2.Zero))
                delta = new(0.01f, 0);

            var distance = delta.Length;
            var effect = 5 * MathF.Pow(totalIntensity, 0.5f) * (1 - distance / range);
            if (effect > 0.01f)
                _recoilSystem.KickCamera(uid, -delta.Normalized * effect);
        }
    }
    #endregion

    #region Processing
    /// <summary>
    ///     Determines whether an entity is blocking a tile or not. (whether it can prevent the tile from being uprooted
    ///     by an explosion).
    /// </summary>
    /// <remarks>
    ///     Used for a variation of <see cref="TurfHelpers.IsBlockedTurf()"/> that makes use of the fact that we have
    ///     already done an entity lookup and don't need to do so again.
    /// </remarks>
    public bool IsBlockingTurf(EntityUid uid)
    {
        if (EntityManager.IsQueuedForDeletion(uid))
            return false;

        if (!TryComp(uid, out IPhysBody? body))
            return false;

        return body.CanCollide && body.Hard && (body.CollisionLayer & (int) CollisionGroup.Impassable) != 0;
    }

    /// <summary>
    ///     Find entities on a grid tile using the EntityLookupComponent and apply explosion effects. 
    /// </summary>
    /// <returns>True if the underlying tile can be uprooted, false if the tile is blocked by a dense entity</returns>
    internal bool ExplodeTile(EntityLookupComponent lookup,
        IMapGrid grid,
        Vector2i tile,
        float intensity,
        DamageSpecifier damage,
        MapCoordinates epicenter,
        HashSet<EntityUid> processed,
        string id)
    {
        var gridBox = new Box2(tile * grid.TileSize, (tile + 1) * grid.TileSize);
        var throwForce = 10 * MathF.Sqrt(intensity);

        // get the entities on a tile. Note that we cannot process them directly, or we get
        // enumerator-changed-while-enumerating errors.
        List<EntityUid> list = new();
        _entityLookup.FastEntitiesIntersecting(lookup, ref gridBox, entity => list.Add(entity));

        // process those entities
        foreach (var entity in list)
        {
            ProcessEntity(entity, epicenter, processed, damage, throwForce, id, false);
        }

        // process anchored entities
        var tileBlocked = false;
        foreach (var entity in grid.GetAnchoredEntities(tile).ToList())
        {
            ProcessEntity(entity, epicenter, processed, damage, throwForce, id, true);
            tileBlocked |= IsBlockingTurf(entity);
        }

        // Next, we get the intersecting entities AGAIN, but purely for throwing. This way, glass shards spawned
        // from windows will be flung outwards, and not stay where they spawned. This is however somewhat
        // unnecessary, and a prime candidate for computational cost-cutting.
        // TODO EXPLOSIONS PERFORMANCE keep this?
        if (!EnablePhysicsThrow)
            return !tileBlocked;

        list.Clear();
        _entityLookup.FastEntitiesIntersecting(lookup, ref gridBox, entity => list.Add(entity));

        foreach (var e in list)
        {
            // Here we only throw, no dealing damage. Containers n such might drop their entities after being destroyed, but
            // they handle their own damage pass-through.
            ProcessEntity(e, epicenter, processed, null, throwForce, id, false);
        }

        return !tileBlocked;
    }

    /// <summary>
    ///     Same as <see cref="ExplodeTile"/>, but for SPAAAAAAACE.
    /// </summary>
    internal void ExplodeSpace(EntityLookupComponent lookup,
        Matrix3 spaceMatrix,
        Matrix3 invSpaceMatrix,
        Vector2i tile,
        float intensity,
        DamageSpecifier damage,
        MapCoordinates epicenter,
        HashSet<EntityUid> processed,
        string id)
    {
        var gridBox = new Box2(tile * DefaultTileSize, (DefaultTileSize, DefaultTileSize));
        var throwForce = 10 * MathF.Sqrt(intensity);
        var worldBox = spaceMatrix.TransformBox(gridBox);
        List<EntityUid> list = new();

        EntityUidQueryCallback callback = uid =>
        {
            if (gridBox.Contains(invSpaceMatrix.Transform(Transform(uid).WorldPosition)))
                list.Add(uid);
        };

        _entityLookup.FastEntitiesIntersecting(lookup, ref worldBox, callback);

        foreach (var entity in list)
        {
            ProcessEntity(entity, epicenter, processed, damage, throwForce, id, false);
        }

        if (!EnablePhysicsThrow)
            return;

        list.Clear();
        _entityLookup.FastEntitiesIntersecting(lookup, ref worldBox, callback);
        foreach (var entity in list)
        {
            ProcessEntity(entity, epicenter, processed, null, throwForce, id, false);
        }
    }

    /// <summary>
    ///     This function actually applies the explosion affects to an entity.
    /// </summary>
    private void ProcessEntity(EntityUid uid, MapCoordinates epicenter, HashSet<EntityUid> processed, DamageSpecifier? damage, float throwForce, string id, bool anchored)
    {
        // check whether this is a valid target, and whether we have already damaged this entity (can happen with
        // explosion-throwing).
        if (!anchored && _containerSystem.IsEntityInContainer(uid) || !processed.Add(uid))
            return;

        // damage
        if (damage != null)
        {
            var ev = new GetExplosionResistanceEvent(id);
            RaiseLocalEvent(uid, ev, false);
            var coeff = Math.Clamp(0, 1 - ev.Resistance, 1);

            if (!MathHelper.CloseTo(0, coeff))
                _damageableSystem.TryChangeDamage(uid, damage * coeff, ignoreResistances: true);
        }

        // throw
        if (!anchored
            && EnablePhysicsThrow
            && !EntityManager.IsQueuedForDeletion(uid)
            && HasComp<ExplosionLaunchedComponent>(uid)
            && TryComp(uid, out TransformComponent? transform))
        {
            uid.TryThrow(transform.WorldPosition - epicenter.Position, throwForce);
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
    struct ExplosionData
    {
        public EntityLookupComponent Lookup;
        public Dictionary<int, HashSet<Vector2i>> TileSets;
        public IMapGrid? MapGrid;
    }

    /// <summary>
    ///     Used to avoid applying explosion effects repeatedly to the same entity. Particularly important if the
    ///     explosion throws this entity, as then it will be moving while the explosion is happening.
    /// </summary>
    public readonly HashSet<EntityUid> ProcessedEntities = new();

    /// <summary>
    ///     This integer tracks how much of this explosion has been processed.
    /// </summary>
    public int CurrentIteration { get; private set; } = 0;

    public readonly ExplosionPrototype ExplosionType;
    public readonly MapCoordinates Epicenter;
    private readonly Matrix3 _spaceMatrix;
    private readonly Matrix3 _invSpaceMatrix;

    private readonly List<ExplosionData> _explosionData = new();
    private readonly List<float> _tileSetIntensity;

    public bool FinishedProcessing;

    // shitty enumerator implementation
    private DamageSpecifier _currentDamage = default!;
    private EntityLookupComponent _currentLookup = default!;
    private IMapGrid? _currentGrid;
    private float _currentIntensity;
    private HashSet<Vector2i>.Enumerator _currentEnumerator;
    private int _currentDataIndex;
    private Dictionary<IMapGrid, List<(Vector2i, Tile)>> _tileUpdateDict = new();

    private readonly ExplosionSystem _system;

    public Explosion(ExplosionSystem system,
        ExplosionPrototype explosionType,
        SpaceExplosion? spaceData,
        List<GridExplosion> gridData,
        List<float> tileSetIntensity,
        MapCoordinates epicenter)
    {
        _system = system;
        ExplosionType = explosionType;
        _tileSetIntensity = tileSetIntensity;
        Epicenter = epicenter;

        var entityMan = IoCManager.Resolve<IEntityManager>();
        var mapMan = IoCManager.Resolve<IMapManager>();

        if (spaceData != null)
        {
            var mapUid = mapMan.GetMapEntityId(epicenter.MapId);

            _explosionData.Add(new()
            {
                TileSets = spaceData.TileSets,
                Lookup = entityMan.GetComponent<EntityLookupComponent>(mapUid),
                MapGrid = null
            });

            _spaceMatrix = spaceData.Matrix;
            _invSpaceMatrix = Matrix3.Invert(spaceData.Matrix);
        }

        foreach (var grid in gridData)
        {
            _explosionData.Add(new()
            {
                TileSets = grid.TileSets,
                Lookup = entityMan.GetComponent<EntityLookupComponent>(grid.Grid.GridEntityId),
                MapGrid = grid.Grid
            });
        }

        TryGetNextTileEnumerator();
    }

    private bool TryGetNextTileEnumerator()
    {
        while (CurrentIteration < _tileSetIntensity.Count)
        {
            _currentIntensity = _tileSetIntensity[CurrentIteration];
            _currentDamage = ExplosionType.DamagePerIntensity * _currentIntensity;

            // for each grid/space tile set
            while (_currentDataIndex < _explosionData.Count)
            {
                // try get any tile hash-set corresponding to this intensity
                var tileSets = _explosionData[_currentDataIndex].TileSets;
                if (!tileSets.TryGetValue(CurrentIteration, out var tileSet))
                {
                    _currentDataIndex++;
                    continue;
                }

                _currentEnumerator = tileSet.GetEnumerator();
                _currentLookup = _explosionData[_currentDataIndex].Lookup;
                _currentGrid = _explosionData[_currentDataIndex].MapGrid;

                _currentDataIndex++;
                return true;
            }

            // this explosion intensity has been fully processed, move to the next one
            CurrentIteration++;
            _currentDataIndex = 0;
        }

        // no more explosion data to process
        FinishedProcessing = true;
        return false;
    }

    private bool MoveNext()
    {
        if (FinishedProcessing)
            return false;

        while (!FinishedProcessing)
        {
            if (_currentEnumerator.MoveNext())
                return true;
            else
                TryGetNextTileEnumerator();
        }

        return false;
    }

    public int Proccess(int processingTarget)
    {
        int processed;

        for (processed = 0; processed < processingTarget; processed++)
        {
            if (_currentGrid != null &&
                _currentGrid.TryGetTileRef(_currentEnumerator.Current, out var tileRef) &&
                !tileRef.Tile.IsEmpty)
            {
                if (!_tileUpdateDict.TryGetValue(_currentGrid, out var tileUpdateList))
                {
                    tileUpdateList = new();
                    _tileUpdateDict[_currentGrid] = tileUpdateList;
                }

                var canDamageFloor = _system.ExplodeTile(_currentLookup,
                    _currentGrid,
                    _currentEnumerator.Current,
                    _currentIntensity,
                    _currentDamage,
                    Epicenter,
                    ProcessedEntities,
                    ExplosionType.ID);

                if (canDamageFloor)
                    _system.DamageFloorTile(tileRef, _currentIntensity, tileUpdateList, ExplosionType);
            }
            else
            {
                _system.ExplodeSpace(_currentLookup,
                    _spaceMatrix,
                    _invSpaceMatrix,
                    _currentEnumerator.Current,
                    _currentIntensity,
                    _currentDamage,
                    Epicenter,
                    ProcessedEntities,
                    ExplosionType.ID);
            }

            if (!MoveNext())
                break;
        }

        foreach (var (grid, list) in _tileUpdateDict)
        {
            if (list.Count > 0)
            {
                grid.SetTiles(list);
            }
        }
        _tileUpdateDict.Clear();

        return processed;
    }
}

