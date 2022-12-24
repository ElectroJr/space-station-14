using Content.Shared.Faction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client.Faction;

public sealed class ClientFactionSystem : FactionSystem
{
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly TransformSystem _xformSys = default!;
    [Dependency] private readonly SpriteSystem _spriteSys = default!;

    private FactionOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FactionComponent, ComponentStartup>(OnFactionStartup);
        _overlay = new(EntityManager, _spriteSys, _xformSys);
        _overlayMan.AddOverlay(_overlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_overlay != null)
            _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnFactionStartup(EntityUid uid, FactionComponent component, ComponentStartup args)
    {
        bool updateView = HasComp<EnableFactionViewComponent>(uid);

        // Faction entity may have been sent to clients AFTER faction membership was updated.
        foreach (var member in EntityQuery<FactionMemberComponent>(true))
        {
            if (member.Factions.Contains(uid))
            {
                component.Members.Add(member.Owner);
                if (updateView)
                    QueueFactionViewUpdate(member.Owner);
            }
        }
    }
}
