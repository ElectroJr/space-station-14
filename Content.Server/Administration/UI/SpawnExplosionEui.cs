using Content.Server.EUI;
using Content.Server.Explosion;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Content.Shared.Explosion;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System.Collections.Generic;
using System.Linq;

namespace Content.Server.Administration.UI
{
    /// <summary>
    ///     Admin Eui for spawning and preview-ing explosions
    /// </summary>
    [UsedImplicitly]
    public sealed class SpawnExplosionEui : BaseEui
    {
        public override void HandleMessage(EuiMessageBase msg)
        {
            if (msg is SpawnExplosionEuiMsg.Close)
            {
                Close();
                return;
            }

            if (msg is not SpawnExplosionEuiMsg.PreviewRequest request)
                return;

            if (request.TotalIntensity <= 0 || request.IntensitySlope <= 0)
                return;

            var mapManager = IoCManager.Resolve<IMapManager>();
            if (!mapManager.TryFindGridAt(request.Epicenter, out var grid))
            {
                // TODO EXPLOSIONS get proper multi-grid explosions working. For now, default to first grid.
                grid = mapManager.GetAllMapGrids(request.Epicenter.MapId).FirstOrDefault();
                if (grid == null)
                    return;
            }

            var sys = EntitySystem.Get<ExplosionSystem>();
            HashSet<Vector2i> initialTiles = new() { grid.TileIndicesFor(request.Epicenter) };

            var (iterationIntensity, data) = sys.GetExplosionTiles(
                grid.Index,
                initialTiles,
                request.TypeId,
                request.TotalIntensity,
                request.IntensitySlope,
                request.MaxIntensity);

            // the explosion event that **would** be sent to all clients, if it were a real explosion.
            var explosion = new ExplosionEvent(request.Epicenter, request.TypeId, data.First().TileSets, iterationIntensity, grid.Index);

            SendMessage(new SpawnExplosionEuiMsg.PreviewData(explosion, request.IntensitySlope, request.TotalIntensity));
            sys.SendEdges(grid.Index);
        }
    }
}
