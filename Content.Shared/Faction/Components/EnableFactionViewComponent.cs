using Robust.Shared.GameStates;

namespace Content.Shared.Faction.Components;

/// <summary>
/// This component enables other entities to view/know about the factions that other entities are a part of. The sum of
/// all factions that are currently visible to some entity are stored seperately in <see cref="FactionViewerComponent"/>.
/// </summary>
/// <remarks>
/// If this component is added to a player entity directly, they will become innately able to see these factions.
/// If this component is added to some piece of equipment, then any wearer of that equipment will be able to see these factions.
/// If this component is added to a faction entity, then all members of that faction will be able to inherently
/// see/know about other members of that faction.
/// </remarks>
// TODO consider splitting this into three separate components?
[Access(typeof(EntitySystems.FactionSystem))]
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class EnableFactionViewComponent : Component
{
    /// <summary>
    /// Set of factions that this player can see (e.g., via some overlay).
    /// </summary>
    [DataField("canSee")]
    [AutoNetworkedField(CloneData = true)]
    public HashSet<EntityUid> CanSee = new();

    /// <summary>
    /// Set of factions that this player can know about (i.e., receive faction membership information in component
    /// states).
    /// </summary>
    [DataField("canKnow")]
    [AutoNetworkedField(CloneData = true)]
    public HashSet<EntityUid> CanKnow = new();
}
