using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;

namespace Content.Client.Explosion
{
    [NetworkedComponent()]
    [RegisterComponent]
    [ComponentReference(typeof(SharedExplosionOverlayComponent))]
    public class ExplosionOverlayComponent : SharedExplosionOverlayComponent
    {
        protected override void Initialize()
        {
            base.Initialize();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (!overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.AddOverlay(new ExplosionOverlay());
        }

        protected override void Shutdown()
        {
            base.Shutdown();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.RemoveOverlay<ExplosionOverlay>();
        }
    }
}
