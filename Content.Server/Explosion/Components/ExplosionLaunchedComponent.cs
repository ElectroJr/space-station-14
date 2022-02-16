using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Server.Explosion.Components
{
    [RegisterComponent]
    public sealed class ExplosionLaunchedComponent : Component
    {
        // TODO EXPLOSION make this a tag? or just get rid of it and launch all unanchored physics entities?
        public override string Name => "ExplosionLaunched";
    }
}
