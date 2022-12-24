using Content.Shared.Faction;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Server.Faction;

/// <summary>
///     This component stores the factions that an entity is a member of.
/// </summary>
public sealed partial class ServerFactionSystem : FactionSystem
{
    [Dependency] private readonly PVSOverrideSystem _pvs = default!;

    private EntityUid _f = default;

    private void OnAttach(PlayerAttachedEvent ev)
    {
        if (!_f.IsValid())
        {
            _f = Spawn(null, MapCoordinates.Nullspace);
            AddComp<FactionComponent>(_f);
            AddComp<EnableFactionViewComponent>(_f).CanSee.Add(_f);
        }
        AddToFaction(ev.Entity, _f);
    }


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerAttachedEvent>(OnAttach);

        SubscribeLocalEvent<FactionMemberComponent, ComponentGetState>(OnGetMemberState);
        SubscribeLocalEvent<FactionComponent, ComponentStartup>(OnFactionStartup);
        SubscribeLocalEvent<FactionViewerComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<FactionViewerComponent, ViewSubscriberAddedEvent>(OnViewerAdded);
    }

    private void OnPlayerAttached(EntityUid uid, FactionViewerComponent component, PlayerAttachedEvent args)
    {
        DirtyFactions.UnionWith(component.CanKnow);
        DirtyFactions.UnionWith(component.CanSee);
    }

    private void OnViewerAdded(EntityUid uid, FactionViewerComponent component, ViewSubscriberAddedEvent args)
    {
        DirtyFactions.UnionWith(component.CanKnow);
        DirtyFactions.UnionWith(component.CanSee);
    }

    private void OnFactionStartup(EntityUid uid, FactionComponent component, ComponentStartup args)
    {
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

        var viewers = new HashSet<EntityUid>();
        viewers.UnionWith(player.ViewSubscriptions);
        if (player.AttachedEntity != null)
            viewers.Add(player.AttachedEntity.Value);

        // players may have more than one viewer (e.g. cameras with sec hud overlays)
        foreach (var viewer in viewers)
        {
            if (!TryComp(viewer, out FactionViewerComponent? viewerComp))
                return;

            canKnow.UnionWith(viewerComp.CanSee);
            canKnow.UnionWith(viewerComp.CanKnow);
        }

        canKnow.IntersectWith(component.Factions);
        args.State = new FactionMemberComponent.FactionMemberState(canKnow);
    }
}
