using Content.Shared.Explosion;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using System.Collections.Generic;

namespace Content.Server.Explosion
{
    public class ExplosionDebugOverlaySystem : EntitySystem
    {
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        public void ClearPreview(ICommonSession session)
        {
            RaiseNetworkEvent(ExplosionOverlayEvent.Empty, session.ConnectedClient);
        }

        public void Preview(ICommonSession session, GridId gridId, Vector2i tile, ExplosionPrototype type, float intensity, float slope, float maxIntensity, HashSet<Vector2i> excluded)
        {
            if (!_mapManager.TryGetGrid(gridId, out var grid))
                return;

            var (tiles, intensityList) = _explosionSystem.GetExplosionTiles(gridId, tile, type.ID, intensity, slope, maxIntensity, excluded);

            ExplosionOverlayEvent args = new(grid.GridTileToWorld(tile), type.ID, tiles, intensityList, gridId, slope, intensity);
            RaiseNetworkEvent(args, session.ConnectedClient);
        }
    }
}
