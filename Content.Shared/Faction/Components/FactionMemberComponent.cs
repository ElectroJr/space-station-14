using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Faction.Components;

/// <summary>
/// This component stores the factions that an entity is a member of. Factions themselves are just null-space entities.
/// </summary>
[Access(typeof(EntitySystems.FactionSystem))]
[RegisterComponent, NetworkedComponent]
public sealed class FactionMemberComponent : Component
{
    [DataField("factions")]
    public HashSet<EntityUid> Factions = new();

    [Serializable, NetSerializable]
    public sealed class FactionMemberState : ComponentState
    {
        public readonly HashSet<EntityUid> Factions;

        public FactionMemberState(HashSet<EntityUid> factions)
        {
            Factions = factions;
        }
    }
}
