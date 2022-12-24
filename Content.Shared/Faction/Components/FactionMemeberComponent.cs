using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Faction;

/// <summary>
///     This component stores the factions that an entity is a member of. Factions are null-space entities.
/// </summary>
[Access(typeof(FactionSystem))]
[RegisterComponent, NetworkedComponent]
public sealed class FactionMemberComponent : Component
{
    [DataField("factions")]
    public HashSet<EntityUid> Factions = new();

    [NetSerializable, Serializable]
    public sealed class FactionMemberState : ComponentState
    {
        public readonly HashSet<EntityUid> Factions = new();

        public FactionMemberState(HashSet<EntityUid> factions)
        {
            Factions = factions;
        }
    }
}
