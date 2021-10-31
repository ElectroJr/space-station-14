using Content.Client.Eui;
using Content.Shared.Administration;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Shared.IoC;

namespace Content.Client.Administration.UI.SpawnExplosion
{
    [UsedImplicitly]
    public sealed class SpawnExplosionEui : BaseEui
    {
        private readonly SpawnExplosionWindow _window;

        public SpawnExplosionEui()
        {
            _window = new SpawnExplosionWindow();
            _window.OnClose += () => SendMessage(new SpawnExplosionEuiMsg.Close());
        }

        public override void Opened()
        {
            base.Opened();
            _window.OpenCentered();
        }

        public override void Closed()
        {
            base.Closed();
            _window.OnClose -= () => SendMessage(new SpawnExplosionEuiMsg.Close());
            _window.Close();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (overlayManager.HasOverlay<ExplosionDebugOverlay>())
                overlayManager.RemoveOverlay<ExplosionDebugOverlay>();
        }

        public override void HandleState(EuiStateBase state)
        {
            var outfitState = (SpawnExplosionEuiState) state;
            _window.TargetEntityId = outfitState.TargetEntityId;

            if (args.Epicenter == MapCoordinates.Nullspace)
            {
                // remove the explosion overlay
                if (_overlayManager.HasOverlay<ExplosionDebugOverlay>())
                    _overlayManager.RemoveOverlay(Overlay);

                Overlay.Tiles.Clear();
                return;
            }

            Overlay.Tiles = args.Tiles;
            Overlay.Intensity = args.Intensity;
            Overlay.Slope = args.Slope;
            Overlay.TotalIntensity = args.TotalIntensity;
            _mapManager.TryGetGrid(args.GridId, out Overlay.Grid);

            if (!_overlayManager.HasOverlay<ExplosionDebugOverlay>())
                _overlayManager.AddOverlay(Overlay);
        }
    }
}
