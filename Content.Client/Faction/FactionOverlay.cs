using Content.Shared.Faction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

namespace Content.Client.Faction;

public sealed class FactionOverlay : Overlay
{
    private readonly IEntityManager _entMan;
    private readonly SpriteSystem _spriteSys;
    private readonly TransformSystem _xformSys;

    // dictionary used for tracking the stacking of faction icons if more than one get drawn.
    private readonly Dictionary<FactionComponent.Location, Dictionary<EntityUid, float>> _iconOffsets = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    private const float ViewportEnlargment = 3;

    public FactionOverlay(IEntityManager entMan, SpriteSystem spriteSys, TransformSystem xformSys)
    {
        _entMan = entMan;
        _spriteSys = spriteSys;
        _xformSys = xformSys;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Shouldn't need to clear cached textures unless the prototypes get reloaded.
        var handle = args.WorldHandle;

        // TODO GET THIS FROM THE IEYE
        EntityUid? eyeEnt = IoCManager.Resolve<IPlayerManager>().LocalPlayer?.ControlledEntity;

        if (!_entMan.TryGetComponent(eyeEnt, out FactionViewerComponent? viewer) || viewer.CanSee.Count == 0)
            return;

        var factionQuery = _entMan.GetEntityQuery<FactionComponent>();
        var xformQuery = _entMan.GetEntityQuery<TransformComponent>();
        var spriteQuery = _entMan.GetEntityQuery<SpriteComponent>();

        foreach (var dict in _iconOffsets.Values)
        {
            dict.Clear();
        }

        List<FactionComponent> factions = new(viewer.CanSee.Count);
        foreach (var faction in viewer.CanSee)
        {
            if (factionQuery.TryGetComponent(faction, out var factionComp))
                factions.Add(factionComp);

        }

        factions.Sort(static (x, y) =>
        {
            var cmp = x.IconPriority.CompareTo(y.IconPriority);
            if (cmp != 0)
                return cmp;

            cmp = x.CreationTick.CompareTo(y.CreationTick);
            if (cmp != 0)
                return cmp;

            return x.Owner.CompareTo(y.Owner);
        });

        foreach (var faction in factions)
        {
            DrawFaction(handle, args, faction, xformQuery, spriteQuery);
        }

        handle.SetTransform(Matrix3.Identity);
    }

    private void DrawFaction(DrawingHandleWorld handle,
        in OverlayDrawArgs args,
        FactionComponent faction,
        EntityQuery<TransformComponent> xformQuery,
        EntityQuery<SpriteComponent> spriteQuery)
    {
        /*if (faction.Icon == null)
            return;

        var texture = _spriteSys.Frame0(faction.Icon);*/
        var offsets = _iconOffsets.GetOrNew(faction.IconLocation);
        var modBounds = args.WorldAABB.Enlarged(ViewportEnlargment);

        foreach (var member in faction.Members)
        {
            if (!xformQuery.TryGetComponent(member, out var xform) || xform.MapID != args.MapId)
                continue;

            var (pos, rot) = _xformSys.GetWorldPositionRotation(xform, xformQuery);
            if (!modBounds.Contains(pos))
                continue;

            if (!spriteQuery.TryGetComponent(member, out var sprite) || !sprite.Visible)
                continue;

            var bounds = sprite.CalculateRotatedBoundingBox(pos, rot);
            handle.DrawRect(bounds, Color.Red.WithAlpha(0.2f));

            var offset = offsets.GetValueOrDefault(member);
            handle.SetTransform(bounds.Transform);
            // handle.DrawTexture(texture, Vector2.Zero);
            // offsets[member] = offset + texture.Height;
        }
    }
}
