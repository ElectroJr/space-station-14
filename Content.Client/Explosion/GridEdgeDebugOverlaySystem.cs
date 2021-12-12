using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client.Explosion;

// Temporary file for testing multi-grid explosions
// TODO EXPLOSIONS REMOVE

public sealed class GridEdgeDebugOverlaySystem : EntitySystem
{
    private GridEdgeDebugOverlay _overlay = default!;

    [Dependency] private readonly IOverlayManager _overlayManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GridEdgeUpdateEvent>(UpdateOverlay);
        _overlay = new GridEdgeDebugOverlay();
    }

    private void UpdateOverlay(GridEdgeUpdateEvent ev)
    {

        if (!_overlayManager.HasOverlay<GridEdgeDebugOverlay>())
            _overlayManager.AddOverlay(_overlay);
        _overlay._gridEdges = ev.GridEdges;
        _overlay._diagGridEdges = ev.DiagGridEdges;
        _overlay.Reference = ev.Reference;
    }
}
