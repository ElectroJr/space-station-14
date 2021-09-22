using Content.Shared.Verbs;
using Robust.Shared.GameObjects;

namespace Content.Server.Explosion
{
    [GlobalVerb]
    class EnableExplosionOverlay : GlobalVerb
    {
        public override bool RequireInteractionRange => false;
        public override bool BlockedByContainers => false;

        public override void GetData(IEntity user, IEntity target, VerbData data)
        {
            data.Text = "Toggle Explosion Overlay";
            data.CategoryData = VerbCategories.Debug;
        }

        public override void Activate(IEntity user, IEntity target)
        {
            if (user.HasComponent<ExplosionOverlayComponent>())
                user.RemoveComponent<ExplosionOverlayComponent>();
            else
                user.AddComponent<ExplosionOverlayComponent>();
        }
    }
}
