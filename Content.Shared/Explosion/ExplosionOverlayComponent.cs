using System;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Explosion
{
    [NetworkedComponent]
    [RegisterComponent]
    public class ExplosionOverlayComponent : Component
    {
        public override string Name => "ExplosionOverlay";

    }
}
