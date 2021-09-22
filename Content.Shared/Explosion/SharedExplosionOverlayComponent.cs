using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Players;
using Robust.Shared.Serialization;

namespace Content.Shared.Explosion
{
    [NetworkedComponent()]
    public abstract class SharedExplosionOverlayComponent : Component
    {
        public override string Name => "ExplosionOverlay";

        public List<HashSet<Vector2i>>? ExplosionData;

        public GridId? GridData;

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

    [Serializable, NetSerializable]
    public class ExplosionOverlayState : ComponentState
    {
        public List<HashSet<Vector2i>>? ExplosionData;

        public GridId? GridData;

        public ExplosionOverlayState(List<HashSet<Vector2i>>? explosionData, GridId? gridData)
        {
            GridData = gridData;
            ExplosionData = explosionData;
        }
    }

}
