using Content.Shared.Faction.Components;
using Robust.Shared.GameStates;

namespace Content.Shared.Faction.EntitySystems;

public abstract partial class FactionSystem
{
    /// <summary>
    /// Set of entities that need to have their viewable factions updated.
    /// </summary>
    protected readonly HashSet<EntityUid> DirtyViewers = new();

    /// <summary>
    /// Factions that need to dirty their member's faction membership components.
    /// </summary>
    protected readonly HashSet<EntityUid> DirtyFactions = new();

    // TODO full game state saves
    // Need to process dirty entities before a map gets saved.

    private void InitializeNetworking()
    {
        SubscribeLocalEvent<FactionMemberComponent, ComponentHandleState>(OnHandleMemberState);
        SubscribeLocalEvent<FactionComponent, ComponentStartup>(OnFactionStartup);
    }

    private void OnHandleMemberState(EntityUid uid, FactionMemberComponent component, ref ComponentHandleState args)
    {
        if (args.Current is FactionMemberComponent.FactionMemberState state)
            SetFactionMembership(uid, state.Factions);
    }

    protected virtual void OnFactionStartup(EntityUid uid, FactionComponent component, ComponentStartup args)
    {
    }

    private void UpdateNetworking()
    {
        foreach (var viewer in DirtyViewers)
        {
            UpdateViewableFactions(viewer);
        }
        DirtyViewers.Clear();

        if (DirtyViewers.Count == 0)
            return;

        var factionQuery = GetEntityQuery<FactionComponent>();
        var memberQuery = GetEntityQuery<FactionMemberComponent>();
        var metaQuery = GetEntityQuery<MetaDataComponent>();

        foreach (var faction in DirtyFactions)
        {
            DirtyAllMembers(faction, factionQuery, memberQuery, metaQuery);
        }
        DirtyFactions.Clear();
    }

    private void DirtyAllMembers(EntityUid uid,
        EntityQuery<FactionComponent> factionQuery,
        EntityQuery<FactionMemberComponent> memberQuery,
        EntityQuery<MetaDataComponent> metaQuery)
    {
        if (!factionQuery.TryGetComponent(uid, out var faction))
            return;

        foreach (var member in faction.Members)
        {
            if (memberQuery.TryGetComponent(member, out var memberComp))
                Dirty(member, memberComp, metaQuery.GetComponent(member));
            else
                Log.Error($"Faction {ToPrettyString(uid)} contained an entity {ToPrettyString(member)} without a faction member component.");
        }
    }
}
