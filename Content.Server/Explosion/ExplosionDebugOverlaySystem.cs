using Content.Server.CombatMode;
using Content.Shared.Explosion;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using System;

namespace Content.Server.Explosion
{
    public class ExplosionDebugOverlaySystem : EntitySystem
    {
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        public int TotalIntensity
        {
            get => _totalIntensity;
            set => _totalIntensity = Math.Max(value, 0);
        }

        public int Slope
        {
            get => _damage;
            set => _damage = Math.Max(value, 1);
        }

        private int _totalIntensity = 1;
        private int _damage = 1;

        private GridId? _currentGrid;
        private Vector2i? _currentEpicenter;

        public override void Initialize()
        {
            base.Initialize();

            CommandBinds.Builder
            .Bind(ContentKeyFunctions.Explode,
                new PointerInputCmdHandler(Explode))
            .Bind(ContentKeyFunctions.Preview,
                new PointerInputCmdHandler(Preview))
            .Bind(ContentKeyFunctions.IncreaseStrength,
                new PointerInputCmdHandler(HandleIncreaseStrength))
            .Bind(ContentKeyFunctions.DecreaseStrength,
                new PointerInputCmdHandler(HandleDecreaseStrength))
            .Bind(ContentKeyFunctions.IncreaseStrengthRelative,
                new PointerInputCmdHandler(HandleIncreaseStrengthRelative))
            .Bind(ContentKeyFunctions.DecreaseStrengthRelative,
                new PointerInputCmdHandler(HandleDecreaseStrengthRelative))
            .Bind(ContentKeyFunctions.IncreaseDamage,
                new PointerInputCmdHandler(HandleIncreaseSlope))
            .Bind(ContentKeyFunctions.DecreaseDamage,
                new PointerInputCmdHandler(HandleDecreaseSlope))
            .Register<ExplosionDebugOverlaySystem>();
        }

        private bool Preview(ICommonSession? session, EntityCoordinates coords, EntityUid uid) => UpdateExplosion(session, coords, uid);

        private bool Explode(ICommonSession? session, EntityCoordinates coords, EntityUid uid) => UpdateExplosion(session, coords, uid, detonate: true);

        private bool UpdateExplosion(ICommonSession? session, EntityCoordinates? coords, EntityUid uid, bool detonate = false)
        {
            if (session == null)
                return false;

            if (coords != null)
            {
                var mapCoords = ((EntityCoordinates) coords).ToMap(_entityManager);
                if (!_mapManager.TryFindGridAt(mapCoords, out var grid))
                {

                    var id = session.AttachedEntity?.Transform.GridID;
                    if (id == null || !_mapManager.TryGetGrid((GridId) id, out grid))
                        return false;
                }

                var epicenterTile = grid.TileIndicesFor(mapCoords);

                if (_currentGrid == grid.Index && _currentEpicenter == epicenterTile)
                {
                    _currentGrid = null;
                    _currentEpicenter = null;
                }
                else
                {
                    _currentGrid = grid.Index;
                    _currentEpicenter = epicenterTile;
                }
            }

            if (_currentGrid == null || _currentEpicenter == null || !_mapManager.TryGetGrid((GridId) _currentGrid, out var grid2))
            {
                RaiseNetworkEvent(ExplosionOverlayEvent.Empty, session.ConnectedClient);
                return true;
            }

            int maxTileIntensity = 999;

            if (detonate)
            {
                _explosionSystem.SpawnExplosion(grid2, (Vector2i) _currentEpicenter, TotalIntensity, Slope, maxTileIntensity);
                RaiseNetworkEvent(ExplosionOverlayEvent.Empty, session.ConnectedClient);
            }
            else
            {
                var (tiles, intensityList) = _explosionSystem.GetExplosionTiles(grid2, (Vector2i) _currentEpicenter, TotalIntensity, Slope, maxTileIntensity);

                if (tiles == null || intensityList == null)
                    return true;

                RaiseNetworkEvent(new ExplosionOverlayEvent(grid2.GridTileToWorld(_currentEpicenter), tiles, intensityList, (GridId) _currentGrid, Slope, TotalIntensity), session.ConnectedClient);
            }

            return true;
        }

        private bool HandleDecreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            TotalIntensity--;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            TotalIntensity++;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseSlope(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Slope++;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleDecreaseSlope(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            Slope--;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseStrengthRelative(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            TotalIntensity = (int) (TotalIntensity * 1.25f);
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleDecreaseStrengthRelative(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            TotalIntensity = (int) (TotalIntensity * 0.8f);
            UpdateExplosion(session, null, uid);
            return true;
        }
    }
}
