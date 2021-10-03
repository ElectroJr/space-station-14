using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.ViewVariables;

namespace Content.Server.Atmos.Components
{
    [RegisterComponent]
    public class AirtightComponent : Component
    {
        public override string Name => "Airtight";

        public (GridId Grid, Vector2i Tile) LastPosition { get; set; }

        [DataField("airBlockedDirection", customTypeSerializer: typeof(FlagSerializer<AtmosDirectionFlags>))]
        [ViewVariables]
        public int InitialAirBlockedDirection { get; set; } = (int) AtmosDirection.All;

        [ViewVariables]
        public int CurrentAirBlockedDirection;

        [DataField("airBlocked")]
        public bool AirBlocked { get; set; } = true;

        [DataField("fixVacuum")]
        public bool FixVacuum { get; set; } = true;

        [ViewVariables]
        [DataField("rotateAirBlocked")]
        public bool RotateAirBlocked { get; set; } = true;

        [ViewVariables]
        [DataField("fixAirBlockedDirectionInitialize")]
        public bool FixAirBlockedDirectionInitialize { get; set; } = true;

        [ViewVariables]
        [DataField("noAirWhenFullyAirBlocked")]
        public bool NoAirWhenFullyAirBlocked { get; set; } = true;

        /// <summary>
        ///     How many multiples of the base explosion damage that this entity can receive before being destroyed.
        /// </summary>
        /// <remarks>
        ///     This is used by the explosion system when figuring out what area is affected by an explosion. If not
        ///     specified, this will be computed upon initialization based on the entities destruction threshold and
        ///     resistance to explosion damage. In order to avoid unnecessary computation at start-up, common structures
        ///     like walls should probably specify this. This value will also be updated whenever the entity takes
        ///     damage.
        /// </remarks>
        [DataField("explosionTolerance")]
        public float ExplosionTolerance;

        public AtmosDirection AirBlockedDirection => (AtmosDirection)CurrentAirBlockedDirection;
    }
}
