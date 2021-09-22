using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server.Explosion.Components
{
    /// <summary>
    ///     This component marks an anchored entity as something that can block explosions.
    /// </summary>
    /// <remarks>
    ///     Without this component, entities are assumed to be permeable to explosion. This component largely exists to
    ///     relax the computational cost of explosion events, by caching the strength of an explosion that is
    ///     required to destroy walls.
    /// </remarks>
    [RegisterComponent]
    public class ExplosionBlocker : Component
    {
        public override string Name => "ExplosionBlocker";

        /// <summary>
        ///     The total multiple of the base explosion group damage that this entity can take before being destroyed.
        ///     After destruction, the tile this entity occupies is no longer blocked for explosion propagation.
        /// </summary>
        /// <remarks>
        ///     If not specified, this will be computed upon initialization based on the entities destruction threshold
        ///     and resistance to explosion damage. In order to avoid unnecessary computation at start-up, common
        ///     structures like walls should definitely specify this. This value will also be updated whenever the
        ///     entity takes damage.
        /// </remarks>
        [DataField("strength")]
        public int Strength;
    }
}
