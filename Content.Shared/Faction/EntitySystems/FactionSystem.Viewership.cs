using Content.Shared.Inventory;
using Robust.Shared.Utility;

namespace Content.Shared.Faction;

public abstract partial class FactionSystem : EntitySystem
{
    public void InitializeViewership()
    {
        SubscribeLocalEvent<FactionMemberComponent, GetViewableFactionsEvent>(OnGetMemberFactionView);
        SubscribeLocalEvent<EnableFactionViewComponent, GetViewableFactionsEvent>(OnGetFactionView);
        SubscribeLocalEvent<EnableFactionViewComponent, InventoryRelayedEvent<GetViewableFactionsEvent>>(OnGetEquipmentFactionView);
        SubscribeLocalEvent<EnableFactionViewComponent, ComponentStartup>(OnEnableViewStartup);
    }

    private void OnEnableViewStartup(EntityUid uid, EnableFactionViewComponent component, ComponentStartup args)
    {
        if (!TryComp(uid, out FactionComponent? faction))
            return;

        foreach (var member in faction.Members)
        {
            QueueFactionViewUpdate(member);
        }
    }

    /// <summary>
    ///     Update the set of factions that an entity can see or know about.
    /// </summary>
    public void QueueFactionViewUpdate(EntityUid uid)
    {
        DirtyViewers.Add(uid);
    }

    /// <summary>
    ///     Update the set of factions that an entity can see or know about.
    /// </summary>
    private void UpdateFactionView(EntityUid uid)
    {
        var ev = new GetViewableFactionsEvent();
        RaiseLocalEvent(uid, ev);

        if (ev.CanKnow.Count == 0 && ev.CanSee.Count == 0)
        {
            RemComp<FactionViewerComponent>(uid);
            return;
        }

        var component = EnsureComp<FactionViewerComponent>(uid);
        DebugTools.Assert(component.Owner == uid);

        var newFactions = new HashSet<EntityUid>(ev.CanKnow);
        newFactions.UnionWith(ev.CanSee);
        newFactions.ExceptWith(component.CanSee);
        newFactions.ExceptWith(component.CanKnow);

        component.CanKnow = ev.CanKnow;
        component.CanSee = ev.CanSee;
        Dirty(component);

        if (newFactions.Count == 0)
            return;

        // This entity can now see/know about new factions. In order to ensure that the faction data gets sent to this
        // client, we need to dirty the faction member components for members of any newly visible faction. This is a
        // bit shit, and but improving this somehow would require PVS & state handling changes.
        DirtyFactions.UnionWith(newFactions);
    }

    /// <summary>
    ///     Passes the event onto factions that this entity is a member of (used to add factions that are inherently
    ///     viewable due to faction membership)..
    /// </summary>
    private void OnGetMemberFactionView(EntityUid uid, FactionMemberComponent component, GetViewableFactionsEvent args)
    {
        foreach (var faction in component.Factions)
        {
            RaiseLocalEvent(faction, args);
        }
    }

    /// <summary>
    ///     Ad factions that are viewable due to some piece of equipment.
    /// </summary>
    private void OnGetEquipmentFactionView(EntityUid uid, EnableFactionViewComponent component, InventoryRelayedEvent<GetViewableFactionsEvent> args)
    {
        args.Args.CanKnow.UnionWith(component.CanKnow);
        args.Args.CanSee.UnionWith(component.CanSee);
    }

    /// <summary>
    ///     Add factions that are viewable due to the enable view component.
    /// </summary>
    private void OnGetFactionView(EntityUid uid, EnableFactionViewComponent component, GetViewableFactionsEvent args)
    {
        args.CanKnow.UnionWith(component.CanKnow);
        args.CanSee.UnionWith(component.CanSee);
    }
}
