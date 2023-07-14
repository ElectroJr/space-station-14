using Robust.Shared.GameStates;

namespace Content.Shared.Faction.Components;

/// <summary>
/// This component determines what faction data gets sent to clients, and what factions can get shown on overlays.
/// </summary>
[Access(typeof(EntitySystems.FactionSystem))]
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionViewerComponent : Component
{
    /// <summary>
    ///     Set of factions that this entity can receive information about.
    /// </summary>
    [DataField("canKnow")]
    [AutoNetworkedField(CloneData = true)]
    public HashSet<EntityUid> CanKnow = new();

    /// <summary>
    ///     Set of factions that this entity can see via the factions overlay.
    /// </summary>
    [DataField("canSee")]
    [AutoNetworkedField(CloneData = true)]
    public HashSet<EntityUid> CanSee = new();
}
