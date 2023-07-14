using Robust.Shared.GameStates;

namespace Content.Shared.Faction.Components;

/// <summary>
/// This component is used to store faction data. Factions are simply null-space entities.
/// </summary>
[Access(typeof(EntitySystems.FactionSystem), Other = AccessPermissions.ReadExecute)]
[RegisterComponent, NetworkedComponent]
public sealed class FactionComponent : Component
{
    /// <summary>
    /// Entities that are a part of this faction.
    /// </summary>
    /// <remarks>
    /// This is not a data-field because membership already gets serialized separately by each member.
    /// </remarks>
    [ViewVariables]
    public HashSet<EntityUid> Members = new();
}
