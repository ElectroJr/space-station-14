using Content.Server.Explosion;
using Content.Shared.Explosion;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;

namespace Content.Client.Explosion
{
    public class ExplosionOverlaySystem : EntitySystem
    {
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        private int _strength;
        private int _damage;

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
                RaiseNetworkEvent(new ExplosionOverlayEvent(null, null, 0, 0), session.ConnectedClient);
                return true;
            }

            var explosion = _explosionSystem.SpawnExplosion(grid2, (Vector2i) _currentTile, _strength, _damage);
            RaiseNetworkEvent(new ExplosionOverlayEvent(explosion, _currentGrid, _strength, _damage), session.ConnectedClient);

            return true;
        }

        private bool HandleDecreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _strength--;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _strength++;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleIncreaseDamage(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _damage++;
            UpdateExplosion(session, null, uid);
            return true;
        }

        private bool HandleDecreaseDamage(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _damage--;
            UpdateExplosion(session, null, uid);
            return true;
        }
    }
}
