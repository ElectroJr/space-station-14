using Content.Shared.Explosion;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    // Temporary file for testing multi-grid explosions
    // TODO EXPLOSIONS REMOVE

    public sealed class GridEdgeDebugOverlaySystem : EntitySystem
    {
        private GridEdgeDebugOverlay _overlay = default!;

        /// <summary>
        ///     For how many seconds should an explosion stay on-screen once it has finished expanding?
        /// </summary>
        public const float ExplosionPersistence = 0.2f;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<GridEdgeUpdateEvent>(UpdateOverlay);

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            _overlay = new GridEdgeDebugOverlay();
            if (!overlayManager.HasOverlay<GridEdgeDebugOverlay>())
                overlayManager.AddOverlay(_overlay);
        }

        private void UpdateOverlay(GridEdgeUpdateEvent ev)
        {
            _overlay.GridEdges = ev.GridEdges;
            _overlay.DiagonalEdges = ev.DiagonalEdges;
            _overlay.Reference = ev.Reference;
        }
    }
}
