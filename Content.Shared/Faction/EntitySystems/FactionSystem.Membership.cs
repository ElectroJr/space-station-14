using Content.Shared.Faction.Components;

namespace Content.Shared.Faction.EntitySystems;

// This partial class provides public methods for modifying faction membership.
public abstract partial class FactionSystem
{
    public void InitializeMembership()
    {
        SubscribeLocalEvent<FactionMemberComponent, ComponentShutdown>(OnMemberShutdown);
        SubscribeLocalEvent<FactionMemberComponent, ComponentStartup>(OnMemberStartup);
    }

    private void OnMemberStartup(EntityUid uid, FactionMemberComponent component, ComponentStartup args)
    {
        var query = GetEntityQuery<FactionComponent>();
        foreach (var faction in component.Factions)
        {
            if (query.TryGetComponent(faction, out var factComp))
                factComp.Members.Add(uid);
        }

        QueueFactionViewUpdate(uid);
    }

    private void OnMemberShutdown(EntityUid uid, FactionMemberComponent component, ComponentShutdown args)
    {
        var query = GetEntityQuery<FactionComponent>();
        foreach (var faction in component.Factions)
        {
            if (query.TryGetComponent(faction, out var factComp))
                factComp.Members.Remove(uid);
        }
    }

    /// <summary>
    ///     Remove an entity from a faction. Returns true if the entity was part of that faction.
    /// </summary>
    public bool RemoveFromFaction(EntityUid uid, EntityUid faction, FactionMemberComponent? memberComp = null)
    {
        if (!Resolve(uid, ref memberComp, false))
            return false;

        if (!memberComp.Factions.Remove(faction))
            return false;

        // client may not yet have been sent the faction entity.
        if (TryComp(faction, out FactionComponent? factionComp))
            factionComp.Members.Remove(uid);

        if (memberComp.Factions.Count == 0)
            RemComp(uid, memberComp);
        else
            Dirty(memberComp);

        QueueFactionViewUpdate(uid);
        return true;
    }

    /// <summary>
    /// Add an entity to a faction. Returns false if the faction does not exist or the entity was already a member.
    /// </summary>
    public bool AddToFaction(EntityUid uid, EntityUid faction)
    {
        var memberComp = EnsureComp<FactionMemberComponent>(uid);
        if (!memberComp.Factions.Add(faction))
            return false;

        // client may not yet have been sent the faction entity.
        if (TryComp(faction, out FactionComponent? factionComp))
            factionComp.Members.Add(uid);

        Dirty(memberComp);
        QueueFactionViewUpdate(uid);
        return true;
    }

    public void SetFactionMembership(EntityUid uid, HashSet<EntityUid> factions)
    {
        if (factions.Count == 0)
        {
            RemComp<FactionMemberComponent>(uid);
            QueueFactionViewUpdate(uid);
            return;
        }

        var memberComp = EnsureComp<FactionMemberComponent>(uid);
        var updateView = false;
        var query = GetEntityQuery<FactionComponent>();

        // Remove old factions
        foreach (var faction in memberComp.Factions)
        {
            if (factions.Contains(faction))
                continue;

            memberComp.Factions.Remove(faction);
            updateView = true;

            if (query.TryGetComponent(faction, out var factionComp))
                factionComp.Members.Remove(uid);
        }

        // Add new factions
        foreach (var faction in factions)
        {
            if (!memberComp.Factions.Add(faction))
                continue;

            updateView = true;
            if (query.TryGetComponent(faction, out var factionComp))
                factionComp.Members.Add(uid);
        }

        if (!updateView)
            return;

        Dirty(memberComp);
        QueueFactionViewUpdate(uid);
    }
}
