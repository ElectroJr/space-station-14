using Content.Server.EUI;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Administration;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System;

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

            var sys = EntitySystem.Get<ExplosionSystem>();
            var mapManager = IoCManager.Resolve<IMapManager>();
            var refGridId = sys.GetReferenceGrid(request.Epicenter, request.TotalIntensity, request.IntensitySlope);

            Vector2i initialTile;
            GridId gridId;
            if (mapManager.TryFindGridAt(request.Epicenter, out var grid) &&
                grid.TryGetTileRef(grid.WorldToTile(request.Epicenter.Position), out var tileRef) &&
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
                    initialTile = mapManager.GetGrid(refGridId).WorldToTile(request.Epicenter.Position);
                }
                else
                {
                    initialTile = new Vector2i(
                        (int) Math.Floor(request.Epicenter.Position.X / ExplosionSystem.DefaultTileSize),
                        (int) Math.Floor(request.Epicenter.Position.Y / ExplosionSystem.DefaultTileSize));
                }
            }

            var (tileSetIntensity, spaceData, gridData) = sys.GetExplosionTiles(
                request.Epicenter.MapId,
                gridId,
                initialTile,
                refGridId,
                request.TypeId,
                request.TotalIntensity,
                request.IntensitySlope,
                request.MaxIntensity);

            // the explosion event that **would** be sent to all clients, if it were a real explosion.
            var explosion = sys.GetExplosionEvent(request.Epicenter, request.TypeId, spaceData, gridData.Values, tileSetIntensity);

            SendMessage(new SpawnExplosionEuiMsg.PreviewData(explosion, request.IntensitySlope, request.TotalIntensity));
        }
    }
}
