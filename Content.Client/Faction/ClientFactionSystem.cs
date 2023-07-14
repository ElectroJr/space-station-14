using Content.Shared.Faction.Components;
using FactionSystem = Content.Shared.Faction.EntitySystems.FactionSystem;

namespace Content.Client.Faction;

public sealed class ClientFactionSystem : FactionSystem
{
    protected override void OnFactionStartup(EntityUid uid, FactionComponent component, ComponentStartup args)
    {
        base.OnFactionStartup(uid, component, args);

        // Faction entity may have been sent to clients AFTER faction membership was updated.
        // In that case we need to enumerate over all faction member components to assemble the list of members of this
        // faction.

        var updateView = HasComp<EnableFactionViewComponent>(uid);
        var query = AllEntityQuery<FactionMemberComponent>();
        while (query.MoveNext(out var memberUid, out var member))
        {
            if (!member.Factions.Contains(uid))
                continue;

            component.Members.Add(memberUid);
            if (updateView)
                QueueFactionViewUpdate(memberUid);
        }
    }
}
