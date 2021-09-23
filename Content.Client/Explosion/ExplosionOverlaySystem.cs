using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.Client.Explosion
{
    public class ExplosionOverlaySystem : EntitySystem
    {
        public ExplosionOverlay? Overlay;

        [Dependency] private readonly IMapManager _mapManager = default!;

        public override void Initialize()
        {
            base.Initialize();


            SubscribeNetworkEvent<ExplosionOverlayEvent>(HandleExplosionOverlay);

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            Overlay = new ExplosionOverlay();
            if (!overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.AddOverlay(Overlay);
        }

        private void HandleExplosionOverlay(ExplosionOverlayEvent args)
        {
            if (Overlay == null)
                return;

            Overlay.Tiles = args.Tiles;
            Overlay.Strength = args.Strength;
            Overlay.TargetTotalStrength = args.TargetTotalStrength;
            Overlay.Damage = args.Damage;

            if (args.Grid == null)
                Overlay.Grid = null;
            else
                _mapManager.TryGetGrid((GridId) args.Grid, out Overlay.Grid);
        }

        public override void Shutdown()
        {
            base.Shutdown();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.RemoveOverlay<ExplosionOverlay>();
        }
    }
}
