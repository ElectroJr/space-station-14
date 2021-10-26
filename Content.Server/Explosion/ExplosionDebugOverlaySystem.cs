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

        public void Preview(ICommonSession session, MapCoordinates coords, ExplosionPrototype type, float intensity, float slope, float maxIntensity, HashSet<Vector2i> excluded)
        {
            if (!_mapManager.TryFindGridAt(coords, out var grid))
                return;

            HashSet<Vector2i> initialTiles = new() { grid.TileIndicesFor(coords) };
            var (tiles, intensityList) = _explosionSystem.GetExplosionTiles(grid.Index, initialTiles, type.ID, intensity, slope, maxIntensity, excluded);

            ExplosionOverlayEvent args = new(coords, type.ID, tiles, intensityList, grid.Index, slope, intensity);
            RaiseNetworkEvent(args, session.ConnectedClient);
        }
    }
}
