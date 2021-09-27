using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.Client.Explosion
{
    public sealed class ExplosionDebugOverlaySystem : EntitySystem
    {
        public ExplosionDebugOverlay? Overlay;

        [Dependency] private readonly IMapManager _mapManager = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<ExplosionOverlayEvent>(HandleExplosionOverlay);

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            Overlay = new ExplosionDebugOverlay();
            if (!overlayManager.HasOverlay<ExplosionDebugOverlay>())
                overlayManager.AddOverlay(Overlay);
        }

        private void HandleExplosionOverlay(ExplosionOverlayEvent args)
        {
            if (Overlay == null)
                return;

            Overlay.Tiles = args.Tiles;
            Overlay.Intensity = args.Intensity;
            Overlay.Damage = args.Damage;
            Overlay.TotalIntensity = args.TotalIntensity;
            _mapManager.TryGetGrid((GridId) args.Grid, out Overlay.Grid);
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
