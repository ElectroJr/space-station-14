using Content.Shared.Verbs;
using Robust.Client.Console;
using Robust.Client.Graphics;
using Robust.Client.ViewVariables;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client.Explosion
{
    [GlobalVerb]
    class EnableExplosionOverlay : GlobalVerb
    {
        public override bool RequireInteractionRange => false;
        public override bool BlockedByContainers => false;

        public override void GetData(IEntity user, IEntity target, VerbData data)
        {
            var groupController = IoCManager.Resolve<IClientConGroupController>();
            if (!groupController.CanViewVar())
            {
                data.Visibility = VerbVisibility.Invisible;
                return;
            }

            data.Text = "Toggle Explosion Overlay";
            data.CategoryData = VerbCategories.Debug;
        }

        public override void Activate(IEntity user, IEntity target)
        {
            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (!overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.AddOverlay(new ExplosionOverlay());
            else
                overlayManager.RemoveOverlay<ExplosionOverlay>();
        }
    }
}
