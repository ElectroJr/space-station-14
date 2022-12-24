using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Faction;

/// <summary>
///     This component is used to store faction data. Factions are simply null-space entities.
/// </summary>
[Access(typeof(FactionSystem), Other = AccessPermissions.ReadExecute)]
[RegisterComponent, NetworkedComponent]
public sealed class FactionComponent : Component
{
    // faction name & description is simply the entity's name & description.

    /// <summary>
    /// Faction icon. Used by the faction overlay.
    /// </summary>
    [DataField("icon")]
    public SpriteSpecifier? Icon;

    /// <summary>
    /// The icon location determines where the faction overlay will draw the icon.
    /// </summary>
    [DataField("iconLocation")]
    public Location IconLocation = Location.TopLeft;

    /// <summary>
    /// The icon priority determines the order in which the faction overlay will draw the various faction icons.
    /// </summary>
    [DataField("iconPriority")]
    public int IconPriority;

    /// <summary>
    /// Entities that are a part of this faction.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> Members = new();

    public enum Location : byte
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3,
    }

    [Serializable, NetSerializable]
    public sealed class FactionState : ComponentState
    {
        public readonly SpriteSpecifier? Icon;
        public readonly Location IconLocation;
        public readonly int IconPriority;

        public FactionState(FactionComponent  comp)
        {
            Icon = comp.Icon;
            IconLocation = comp.IconLocation;
            IconPriority = comp.IconPriority;
        }
    }
}
