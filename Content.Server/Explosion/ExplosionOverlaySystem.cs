using Content.Server.CombatMode;
using Content.Server.Explosion;
using Content.Shared.Explosion;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using Robust.Shared.Timing;
using System;

namespace Content.Client.Explosion
{
    public class ExplosionOverlaySystem : EntitySystem
    {
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        public int Strength
        {
            get => _strength;
            set => _strength = Math.Max(value, 0);
        }
        public int Damage
        {
            get => _damage;
            set => _damage = Math.Max(value, 1);
        }

        private int _strength = 1;
        private int _damage = 1;

        private GridId? _currentGrid;
        private Vector2i? _currentTile;

        public override void Initialize()
        {
            base.Initialize();

            CommandBinds.Builder
            .Bind(ContentKeyFunctions.Explode,
                new PointerInputCmdHandler(Explode))
            .Bind(ContentKeyFunctions.IncreaseStrength,
                new PointerInputCmdHandler(HandleIncreaseStrength))
            .Bind(ContentKeyFunctions.DecreaseStrength,
                new PointerInputCmdHandler(HandleDecreaseStrength))
            .Bind(ContentKeyFunctions.IncreaseStrengthRelative,
                new PointerInputCmdHandler(HandleIncreaseStrengthRelative))
            .Bind(ContentKeyFunctions.DecreaseStrengthRelative,
                new PointerInputCmdHandler(HandleDecreaseStrengthRelative))
            .Bind(ContentKeyFunctions.IncreaseDamage,
                new PointerInputCmdHandler(HandleIncreaseDamage))
            .Bind(ContentKeyFunctions.DecreaseDamage,
                new PointerInputCmdHandler(HandleDecreaseDamage))
            .Register<ExplosionOverlaySystem>();
        }

        private bool Explode(ICommonSession? session, EntityCoordinates coords, EntityUid uid) => UpdateExplosion(session, coords, uid);

        private bool UpdateExplosion(ICommonSession? session, EntityCoordinates? coords, EntityUid uid)
        {
            if (session == null)
                return false;

            if (coords != null)
            {
                var mapCoords = ((EntityCoordinates) coords).ToMap(_entityManager);
                if (!_mapManager.TryFindGridAt(mapCoords, out var grid))
                    return false;

                var epicenterTile = grid.TileIndicesFor(mapCoords);

                if (_currentGrid == grid.Index && _currentTile == epicenterTile)
                {
                    _currentGrid = null;
                    _currentTile = null;
                }
                else
                {
                    _currentGrid = grid.Index;
                    _currentTile = epicenterTile;
                }
            }

            if (_currentGrid == null || _currentTile == null || !_mapManager.TryGetGrid((GridId) _currentGrid, out var grid2))
            {
                RaiseNetworkEvent(new ExplosionOverlayEvent(null, null, null, 0, 0), session.ConnectedClient);
                return true;
            }


            if (session.AttachedEntity != null)
            {
                if (session.AttachedEntity.TryGetComponent(out CombatModeComponent? combat))
                {
                    if (combat.IsInCombatMode)
                    {
                        RaiseNetworkEvent(new ExplosionOverlayEvent(null, null, _currentGrid, Strength, Damage), session.ConnectedClient);
                        _explosionSystem.SpawnExplosion(grid2, (Vector2i) _currentTile, Strength, Damage);
                    }
                    else
                    {
                        var (tiles, intensityList) = _explosionSystem.GetExplosionTiles(grid2, (Vector2i) _currentTile, Strength, Damage);
                        RaiseNetworkEvent(new ExplosionOverlayEvent(tiles, intensityList, _currentGrid, Strength, Damage), session.ConnectedClient);
                        return true;
                    }
                }
            }
            

            

            return true;
        }

        private bool HandleDecreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Strength--;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Strength++;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseDamage(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Damage++;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleDecreaseDamage(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Damage--;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseStrengthRelative(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Strength = (int) (Strength * 1.25f);
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleDecreaseStrengthRelative(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Strength = (int) (Strength * 0.8f);
            UpdateExplosion(session, null, uid);
            return true;
        }
    }
}
