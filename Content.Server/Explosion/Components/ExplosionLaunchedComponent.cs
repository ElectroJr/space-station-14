using Robust.Shared.GameObjects;

namespace Content.Server.Explosion.Components
{
    [RegisterComponent]
    public class ExplosionLaunchedComponent : Component
    {
        // TODO EXPLOSION make this a tag? or just get rid of it and launch all unanchored physics entities?
        public override string Name => "ExplosionLaunched";
    }
}
