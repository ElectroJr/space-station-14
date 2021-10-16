using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.Client.Explosion
{
    public sealed class ExplosionDebugOverlaySystem : EntitySystem
    {
        public ExplosionDebugOverlay Overlay = new ();

        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IOverlayManager _overlayManager = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<ExplosionOverlayEvent>(HandleExplosionOverlay);
        }

        private void HandleExplosionOverlay(ExplosionOverlayEvent args)
        {
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
            _mapManager.TryGetGrid(args.Grid, out Overlay.Grid);

            if (!_overlayManager.HasOverlay<ExplosionDebugOverlay>())
                _overlayManager.AddOverlay(Overlay);
        }

        public override void Shutdown()
        {
            base.Shutdown();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (overlayManager.HasOverlay<ExplosionDebugOverlay>())
                overlayManager.RemoveOverlay<ExplosionDebugOverlay>();
        }
    }
}
