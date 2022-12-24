using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Faction;

/// <summary>
///     This component enables an entity or wearer to view/know about the factions that other entities are a part of.
/// </summary>
/// <remarks>
///     If this component is added to a faction entity, then all members will be able to inherently see/know about other members of that faction.
/// </remarks>
[Access(typeof(FactionSystem))]
[RegisterComponent, NetworkedComponent]
public sealed class EnableFactionViewComponent : Component
{
    [DataField("canSee")]
    public HashSet<EntityUid> CanSee = new();

    [DataField("canKnow")]
    public HashSet<EntityUid> CanKnow = new();

    [NetSerializable, Serializable]
    public sealed class EnableFactionViewState : ComponentState
    {
        public readonly HashSet<EntityUid> CanSee = new();
        public readonly HashSet<EntityUid> CanKnow = new();

        public EnableFactionViewState(EnableFactionViewComponent comp)
        {
            CanSee = comp.CanSee;
            CanKnow = comp.CanKnow;
        }
    }
}
