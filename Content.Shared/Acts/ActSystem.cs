using System;
using System.Linq;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Shared.Acts
{
    /// <summary>
    /// This interface gives components behavior on getting destroyed.
    /// </summary>
    public interface IDestroyAct
    {
        /// <summary>
        /// Called when object is destroyed
        /// </summary>
        void OnDestroy(DestructionEventArgs eventArgs);
    }

    public class DestructionEventArgs : EntityEventArgs
    {
        public IEntity Owner { get; set; } = default!;
    }

    public class BreakageEventArgs : EventArgs
    {
        public IEntity Owner { get; set; } = default!;
    }

    public interface IBreakAct
    {
        /// <summary>
        /// Called when object is broken
        /// </summary>
        void OnBreak(BreakageEventArgs eventArgs);
    }

    [UsedImplicitly]
    public sealed class ActSystem : EntitySystem
    {
        public void HandleDestruction(IEntity owner)
        {
            var eventArgs = new DestructionEventArgs
            {
                Owner = owner
            };

            var destroyActs = owner.GetAllComponents<IDestroyAct>().ToList();

            foreach (var destroyAct in destroyActs)
            {
                destroyAct.OnDestroy(eventArgs);
            }

            owner.QueueDelete();
        }

        public void HandleBreakage(IEntity owner)
        {
            var eventArgs = new BreakageEventArgs
            {
                Owner = owner,
            };
            var breakActs = owner.GetAllComponents<IBreakAct>().ToList();
            foreach (var breakAct in breakActs)
            {
                breakAct.OnBreak(eventArgs);
            }
        }
    }
}
