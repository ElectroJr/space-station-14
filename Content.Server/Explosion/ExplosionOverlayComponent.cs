using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;

namespace Content.Server.Explosion
{
    [NetworkedComponent()]
    [RegisterComponent]
    [ComponentReference(typeof(SharedExplosionOverlayComponent))]
    public class ExplosionOverlayComponent : SharedExplosionOverlayComponent
    {

        protected override void Initialize()
        {
            base.Initialize();
            Dirty();
        }

    }
}
