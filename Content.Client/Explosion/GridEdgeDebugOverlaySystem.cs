using Content.Shared.CCVar;
using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client.Explosion;

// Temporary file for testing multi-grid explosions
// TODO EXPLOSIONS REMOVE

public sealed class GridEdgeDebugOverlaySystem : EntitySystem
{
    private GridEdgeDebugOverlay _overlay = default!;

    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<GridEdgeUpdateEvent>(UpdateOverlay);
        _overlay = new GridEdgeDebugOverlay();

        _cfg.OnValueChanged(CCVars.ExplosionDrawEdges, value => _overlay.DrawGridEdges = value, true);
        _cfg.OnValueChanged(CCVars.ExplosionDrawLocalEdges, value => _overlay.DrawLocalEdges = value, true);

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
