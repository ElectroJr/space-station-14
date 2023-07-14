using Content.Shared.Faction.Components;
using Content.Shared.Inventory;

namespace Content.Shared.Faction.EntitySystems;

public abstract partial class FactionSystem
{
    public void InitializeViewership()
    {
        SubscribeLocalEvent<FactionMemberComponent, GetViewableFactionsEvent>(OnGetMemberFactionView);
        SubscribeLocalEvent<EnableFactionViewComponent, GetViewableFactionsEvent>(OnGetFactionView);
        SubscribeLocalEvent<EnableFactionViewComponent, InventoryRelayedEvent<GetViewableFactionsEvent>>(OnGetEquipmentFactionView);
        SubscribeLocalEvent<EnableFactionViewComponent, ComponentStartup>(OnEnableViewStartup);

        // TODO add public methods to add or remove factions from an EnableFactionViewComponent
        // Would require keeping track of all entities that an enable-view component is currently granting viewership for
        // Which seems quite convoluted.
        //
        // Maybe split enable-view into separate components.
        // Maybe make can-view-self a simple field on FactionComponent.
    }

    private void OnEnableViewStartup(EntityUid uid, EnableFactionViewComponent component, ComponentStartup args)
    {
        if (TryComp(uid, out FactionComponent? faction))
            DirtyViewers.UnionWith(faction.Members);
    }

    public void QueueFactionViewUpdate(EntityUid uid)
    {
        DirtyViewers.Add(uid);
    }

    /// <summary>
    /// Update the set of factions that an entity can see or know about.
    /// </summary>
    private void UpdateViewableFactions(EntityUid uid)
    {
        var ev = new GetViewableFactionsEvent();
        RaiseLocalEvent(uid, ev);
        ev.CanKnow.UnionWith(ev.CanSee);

        if (ev.CanKnow.Count == 0 && ev.CanSee.Count == 0)
        {
            RemComp<FactionViewerComponent>(uid);
            return;
        }

        var component = EnsureComp<FactionViewerComponent>(uid);

        // Get the set of NEWLY visible factions.
        var newFactions = new HashSet<EntityUid>(ev.CanKnow);
        newFactions.ExceptWith(component.CanKnow);

        // Update visible factions.
        component.CanKnow = ev.CanKnow;
        component.CanSee = ev.CanSee;
        Dirty(component);

        if (newFactions.Count == 0)
            return;

        // This entity can now see/know about new factions. In order to ensure that the faction data gets sent to this
        // client, we need to dirty the faction member components for all members of all newly visible faction. This is
        // a bit shit, and but improving this somehow would require PVS & state handling changes.
        DirtyFactions.UnionWith(newFactions);
    }

    // Relay the get-viewable event to faction entities to get information about factions that are viable as
    // a result of being a member of that faction.
    private void OnGetMemberFactionView(EntityUid uid, FactionMemberComponent component, GetViewableFactionsEvent args)
    {
        foreach (var faction in component.Factions)
        {
            RaiseLocalEvent(faction, args);
        }
    }

    // Add factions that are visible due to some piece of equipment.
    private void OnGetEquipmentFactionView(EntityUid uid, EnableFactionViewComponent component, InventoryRelayedEvent<GetViewableFactionsEvent> args)
    {
        args.Args.CanKnow.UnionWith(component.CanKnow);
        args.Args.CanSee.UnionWith(component.CanSee);
    }

    // Add innately viewable factions.
    private void OnGetFactionView(EntityUid uid, EnableFactionViewComponent component, GetViewableFactionsEvent args)
    {
        args.CanKnow.UnionWith(component.CanKnow);
        args.CanSee.UnionWith(component.CanSee);
    }
}
