using Robust.Shared.GameStates;

namespace Content.Shared.Faction;

/// <summary>
///     This component stores the factions that an entity is a member of.
/// </summary>
public abstract partial class FactionSystem : EntitySystem
{
    protected readonly HashSet<EntityUid> DirtyFactions = new();
    protected readonly HashSet<EntityUid> DirtyViewers = new();

    private void InitializeNetworking()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionMemberComponent, ComponentHandleState>(OnHandleMemberState);

        SubscribeLocalEvent<FactionComponent, ComponentGetState>(OnGetFactionState);
        SubscribeLocalEvent<FactionComponent, ComponentHandleState>(OnHandleFactionState);

        SubscribeLocalEvent<EnableFactionViewComponent, ComponentGetState>(OnGetEnableViewerState);
        SubscribeLocalEvent<EnableFactionViewComponent, ComponentHandleState>(OnHandleEnableViewertate);

        SubscribeLocalEvent<FactionViewerComponent, ComponentGetState>(OnGetViewerState);
        SubscribeLocalEvent<FactionViewerComponent, ComponentHandleState>(OnHandleViewerState);
    }

    private void UpdateNetworking()
    {
        var query = GetEntityQuery<FactionMemberComponent>();

        foreach (var viewer in DirtyViewers)
        {
            UpdateFactionView(viewer);
        }
        DirtyViewers.Clear();

        foreach (var faction in DirtyFactions)
        {
            DirtyAllMembers(faction, null, query);
        }
        DirtyFactions.Clear();
    }

    public void DirtyAllMembers(EntityUid uid, FactionComponent? faction = null, EntityQuery<FactionMemberComponent>? query = null)
    {
        if (!Resolve(uid, ref faction))
            return;

        query ??= GetEntityQuery<FactionMemberComponent>();
        foreach (var member in faction.Members)
        {
            if (!query.Value.TryGetComponent(member, out var memberComp))
            {
                Logger.Error($"Faction {ToPrettyString(uid)} contained an entity {ToPrettyString(member)} without a faction member component.");
                continue;
            }

            Dirty(memberComp);
        }
    }

    private void OnGetFactionState(EntityUid uid, FactionComponent component, ref ComponentGetState args)
    {
        args.State = new FactionComponent.FactionState(component);
    }

    private void OnHandleFactionState(EntityUid uid, FactionComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not FactionComponent.FactionState state)
            return;

        component.Icon = state.Icon;
        component.IconLocation = state.IconLocation;
        component.IconPriority = state.IconPriority;
    }

    private void OnHandleViewerState(EntityUid uid, FactionViewerComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not FactionViewerComponent.FactionViewerState state)
            return;

        component.CanKnow = new(state.CanKnow);
        component.CanSee = new(state.CanSee);
    }

    private void OnGetViewerState(EntityUid uid, FactionViewerComponent component, ref ComponentGetState args)
    {
        args.State = new FactionViewerComponent.FactionViewerState(component.CanKnow, component.CanSee);
    }

    private void OnGetEnableViewerState(EntityUid uid, EnableFactionViewComponent component, ref ComponentGetState args)
    {
        args.State = new EnableFactionViewComponent.EnableFactionViewState(component);
    }

    private void OnHandleEnableViewertate(EntityUid uid, EnableFactionViewComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not EnableFactionViewComponent.EnableFactionViewState state)
            return;

        component.CanSee = new(state.CanSee);
        component.CanKnow= new(state.CanKnow);
    }

    private void OnHandleMemberState(EntityUid uid, FactionMemberComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not FactionMemberComponent.FactionMemberState state)
            return;

        SetFactionMembership(uid, state.Factions, component);
    }
}
