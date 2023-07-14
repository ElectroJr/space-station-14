using Content.Shared.Inventory;

namespace Content.Shared.Faction;

public sealed class GetViewableFactionsEvent : EntityEventArgs, IInventoryRelayEvent
{
    public readonly HashSet<EntityUid> CanKnow = new();
    public readonly HashSet<EntityUid> CanSee = new();
    public SlotFlags TargetSlots => ~SlotFlags.POCKET;
}
