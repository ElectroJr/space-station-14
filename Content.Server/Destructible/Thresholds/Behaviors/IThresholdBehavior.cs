using Robust.Shared.GameObjects;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    public interface IThresholdBehavior
    {
        /// <summary>
        ///     Executes this behavior.
        /// </summary>
        /// <param name="owner">The entity that owns this behavior.</param>
        /// <param name="system">
        ///     An instance of <see cref="DestructibleSystem"/> to pull dependencies
        ///     and other systems from.
        /// </param>
        /// <returns>
        ///     Returns true if destructible system should continue executing behaviors. Returns false if it should
        ///     terminate. Useful for early-termination when taking excess damage and you want to avoid trigging
        ///     low-damage threshold behaviors.
        /// </returns>
        bool Execute(EntityUid owner, DestructibleSystem system);
    }
}
