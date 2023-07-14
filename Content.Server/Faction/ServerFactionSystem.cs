using Content.Shared.Faction.Components;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;
using FactionSystem = Content.Shared.Faction.EntitySystems.FactionSystem;

namespace Content.Server.Faction;

/// <summary>
///     This component stores the factions that an entity is a member of.
/// </summary>
public sealed class ServerFactionSystem : FactionSystem
{
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionMemberComponent, ComponentGetState>(OnGetMemberState);
        SubscribeLocalEvent<FactionViewerComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<FactionViewerComponent, ViewSubscriberAddedEvent>(OnViewerAdded);
    }

    private void OnPlayerAttached(EntityUid uid, FactionViewerComponent component, PlayerAttachedEvent args)
    {
        DebugTools.Assert(component.CanSee.IsSubsetOf(component.CanKnow));
        DirtyFactions.UnionWith(component.CanKnow);
    }

    private void OnViewerAdded(EntityUid uid, FactionViewerComponent component, ViewSubscriberAddedEvent args)
    {
        DebugTools.Assert(component.CanSee.IsSubsetOf(component.CanKnow));
        DirtyFactions.UnionWith(component.CanKnow);
    }

    protected override void OnFactionStartup(EntityUid uid, FactionComponent component, ComponentStartup args)
    {
        base.OnFactionStartup(uid, component, args);
        // TODO change this so that only known factions get added to a players override.
        // Otherwise, need to ensure that every round always has every faction.
        // But that doesn't work for station-specific factions (e.g., per station criminal status).
        _pvs.AddGlobalOverride(uid);
    }

    private void OnGetMemberState(EntityUid uid, FactionMemberComponent component, ref ComponentGetState args)
    {
        if (args.Player is not IPlayerSession player)
        {
            // spectators get full component data.
            args.State = new FactionMemberComponent.FactionMemberState(component.Factions);
            return;
        }

        HashSet<EntityUid> canKnow = new();

        // Get full collection of viewers
        // E.g., player may be using a camera with a built in sec hud
        var viewers = new HashSet<EntityUid>();
        viewers.UnionWith(player.ViewSubscriptions);
        if (player.AttachedEntity != null)
            viewers.Add(player.AttachedEntity.Value);

        // Get viewable factions for each viewer.
        foreach (var viewer in viewers)
        {
            if (!TryComp(viewer, out FactionViewerComponent? viewerComp))
                continue;

            DebugTools.Assert(viewerComp.CanSee.IsSubsetOf(viewerComp.CanKnow));
            canKnow.UnionWith(viewerComp.CanKnow);
        }

        // Finally, the state the player gets given contains only factions that the player can know about.
        canKnow.IntersectWith(component.Factions);
        args.State = new FactionMemberComponent.FactionMemberState(canKnow);
    }
}
