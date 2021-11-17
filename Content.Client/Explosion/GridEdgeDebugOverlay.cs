using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    // Temporary file for testing multi-grid explosions
    // TODO EXPLOSIONS REMOVE

    [UsedImplicitly]
    public sealed class GridEdgeDebugOverlay : Overlay
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        public Dictionary<GridId, HashSet<Vector2i>> GridEdges = new();

        public GridEdgeDebugOverlay()
        {
            IoCManager.InjectDependencies(this);
        }

        protected override void Draw(in OverlayDrawArgs args)
        {

            var worldBounds = _eyeManager.GetWorldViewbounds();
            var handle = args.WorldHandle;

            foreach (var (gridId, edges) in GridEdges)
            {
                if (!_mapManager.TryGetGrid(gridId, out var grid))
                    continue;

                if (grid.ParentMapId != _eyeManager.CurrentMap)
                    continue;

                DrawEdges(grid, edges, handle, worldBounds);
            }
        }

        private void DrawEdges(IMapGrid grid, HashSet<Vector2i> edges, DrawingHandleWorld handle, Box2Rotated worldBounds)
        {
            var gridXform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
            var gridBounds = gridXform.InvWorldMatrix.TransformBox(worldBounds);

            foreach (var tile in edges)
            {
                // is the center of this tile visible to the user?
                if (!gridBounds.Contains((Vector2) tile + 0.5f))
                    continue;

                var worldCenter = gridXform.WorldMatrix.Transform((Vector2) tile + 0.5f);
                var worldBox = Box2.UnitCentered.Translated(worldCenter);
                var rotatedBox = new Box2Rotated(worldBox, gridXform.WorldRotation, worldCenter);

                handle.DrawRect(rotatedBox, Color.Yellow, false);
            }
        }
    }
}
