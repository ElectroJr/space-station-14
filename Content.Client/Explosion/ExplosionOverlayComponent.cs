using System;
using System.Collections.Generic;
using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using Robust.Shared.Serialization;

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


        public override ComponentState GetComponentState(ICommonSession player)
        {
            return new ExplosionOverlayState(ExplosionData, GridData);
        }

        public override void HandleComponentState(ComponentState? curState, ComponentState? nextState)
        {
            if (curState is not ExplosionOverlayState state)
                return;

            GridData = state.GridData;
            ExplosionData = state.ExplosionData;
        }
    }
}
