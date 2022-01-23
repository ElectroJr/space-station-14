using Content.Server.EUI;
using Content.Server.Explosion.EntitySystems;
using Content.Shared.Administration;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Timing;

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

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var results = sys.GetExplosionTiles(
                request.Epicenter,
                request.TypeId,
                request.TotalIntensity,
                request.IntensitySlope,
                request.MaxIntensity);

            if (results == null)
                return;

            var (iterationIntensity, spaceData, gridData, spaceMatrix) = results.Value;

            Logger.Info($"Generated explosion preview in {stopwatch.Elapsed.TotalMilliseconds}ms");

            // the explosion event that **would** be sent to all clients, if it were a real explosion.
            var explosion = sys.GetExplosionEvent(request.Epicenter, request.TypeId, spaceMatrix, spaceData, gridData.Values, iterationIntensity);

            SendMessage(new SpawnExplosionEuiMsg.PreviewData(explosion, request.IntensitySlope, request.TotalIntensity));
        }
    }
}
