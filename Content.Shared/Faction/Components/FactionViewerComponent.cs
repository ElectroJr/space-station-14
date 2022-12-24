using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Faction;

/// <summary>
///     This component determines what faction data gets sent to clients, and what factions get shown on a client's
///     faction overlay.
/// </summary>
[Access(typeof(FactionSystem))]
[RegisterComponent, NetworkedComponent]
public sealed class FactionViewerComponent : Component
{
    /// <summary>
    ///     Set of factions that this entity can receive information about.
    /// </summary>
    [DataField("canKnow")]
    public HashSet<EntityUid> CanKnow = new();

    /// <summary>
    ///     Set of factions that this entity can see via the factions overlay.
    /// </summary>
    [DataField("canSee")]
    public HashSet<EntityUid> CanSee = new();

    [NetSerializable, Serializable]
    public sealed class FactionViewerState : ComponentState
    {
        public readonly HashSet<EntityUid> CanKnow = new();
        public readonly HashSet<EntityUid> CanSee = new();

        public FactionViewerState(HashSet<EntityUid> canKnow, HashSet<EntityUid> canSee)
        {
            CanKnow = canKnow;
            CanSee = canSee;
        }
    }
}
