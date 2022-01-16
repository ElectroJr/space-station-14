using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Destructible.Thresholds.Behaviors
{
    /// <summary>
    ///     Drop all items from all containers
    /// </summary>
    [DataDefinition]
    public class EmptyAllContainersBehaviour : IThresholdBehavior
    {
        public bool Execute(EntityUid owner, DestructibleSystem system)
        {
            if (!system.EntityManager.TryGetComponent<ContainerManagerComponent>(owner, out var containerManager))
                return true;

            foreach (var container in containerManager.GetAllContainers())
            {
                container.EmptyContainer(true, system.EntityManager.GetComponent<TransformComponent>(owner).Coordinates);
            }

            return true;
        }
    }
}
