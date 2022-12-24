using Content.Shared.Inventory.Events;

namespace Content.Shared.Faction;

/// <summary>
///     This component stores the factions that an entity is a member of.
/// </summary>
public abstract partial class FactionSystem : EntitySystem
{
    private void InitializeEquipment()
    {
        SubscribeLocalEvent<EnableFactionViewComponent, GotEquippedEvent>(OnGotEquipped);
        SubscribeLocalEvent<EnableFactionViewComponent, GotUnequippedEvent>(OnGotUnequipped);
    }

    private void OnGotUnequipped(EntityUid uid, EnableFactionViewComponent component, GotUnequippedEvent args)
    {
        QueueFactionViewUpdate(uid);
    }

    private void OnGotEquipped(EntityUid uid, EnableFactionViewComponent component, GotEquippedEvent args)
    {
        QueueFactionViewUpdate(uid);
    }
}
