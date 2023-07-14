using Content.Shared.Faction.Components;
using Content.Shared.Inventory.Events;

namespace Content.Shared.Faction.EntitySystems;

public abstract partial class FactionSystem
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
