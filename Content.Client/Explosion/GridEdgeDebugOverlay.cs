using JetBrains.Annotations;
using Robust.Client.Graphics;
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
        public GridId Reference;

        public static readonly Matrix3 Offset = new(
            1, 0, 0.25f,
            0, 1, 0.25f,
            0, 0, 1
        );

        public GridEdgeDebugOverlay()
        {
            IoCManager.InjectDependencies(this);
        }

        private HashSet<Vector2i> TransformGridEdge(HashSet<Vector2i> edges, GridId source, GridId target)
        {
            var _entityManager = IoCManager.Resolve<IEntityManager>();
            if (source == target)
                return edges;

            HashSet<Vector2i> targetEdges = new();

            if (!_mapManager.TryGetGrid(source, out var sourceGrid) ||
                !_mapManager.TryGetGrid(target, out var targetGrid) ||
                !_entityManager.TryGetComponent(sourceGrid.GridEntityId, out TransformComponent sourceTransform) ||
                !_entityManager.TryGetComponent(targetGrid.GridEntityId, out TransformComponent targetTransform))
            {
                return targetEdges;
            }

            var angle = sourceTransform.WorldRotation - targetTransform.WorldRotation;
            var matrix = Offset * sourceTransform.WorldMatrix * targetTransform.InvWorldMatrix;
            var offset1 = angle.RotateVec((0, 0.5f));
            var offset2 = angle.RotateVec((0.5f, 0));

            foreach (var tile in edges)
            {
                var transformed = matrix.Transform(tile);
                targetEdges.Add(new((int) MathF.Floor(transformed.X), (int) MathF.Floor(transformed.Y)));
                transformed += offset1;
                targetEdges.Add(new((int) MathF.Floor(transformed.X), (int) MathF.Floor(transformed.Y)));
                transformed += offset2;
                targetEdges.Add(new((int) MathF.Floor(transformed.X), (int) MathF.Floor(transformed.Y)));
                transformed -= offset1;
                targetEdges.Add(new((int) MathF.Floor(transformed.X), (int) MathF.Floor(transformed.Y)));
            }

            return targetEdges;
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            var worldBounds = _eyeManager.GetWorldViewbounds();
            var handle = args.WorldHandle;

            if (!_mapManager.TryGetGrid(Reference, out var referenceGrid))
                return;

            foreach (var (gridId, edges) in GridEdges)
            {
                if (!_mapManager.TryGetGrid(gridId, out var grid))
                    continue;

                if (grid.ParentMapId != _eyeManager.CurrentMap)
                    continue;

                DrawEdges(grid, edges, handle, worldBounds, Color.Yellow);
                DrawNode(grid, edges, handle, worldBounds, Color.Yellow);
                DrawEdges(referenceGrid, TransformGridEdge(edges, gridId, Reference), handle, worldBounds, Color.Red);
            }
        }

        private void DrawEdges(IMapGrid grid, HashSet<Vector2i> edges, DrawingHandleWorld handle, Box2Rotated worldBounds, Color color)
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

                handle.DrawRect(rotatedBox, color, false);
            }
        }

        private void DrawNode(IMapGrid grid, HashSet<Vector2i> edges, DrawingHandleWorld handle, Box2Rotated worldBounds, Color color)
        {
            var gridXform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
            var gridBounds = gridXform.InvWorldMatrix.TransformBox(worldBounds);
            var matrix = gridXform.WorldMatrix;

            foreach (var tile in edges)
            {
                // is the center of this tile visible to the user?
                if (!gridBounds.Contains((Vector2) tile + 0.5f))
                    continue;

                var x1 = ((Vector2) tile) + 0.25f;
                var x2 = ((Vector2) tile) + (0.75f, 0.25f);
                var x3 = ((Vector2) tile) + (0.25f, 0.75f);
                var x4 = ((Vector2) tile) + 0.75f;

                handle.DrawCircle(matrix.Transform(x1), 0.02f, color, true);
                handle.DrawCircle(matrix.Transform(x2), 0.02f, color, true);
                handle.DrawCircle(matrix.Transform(x3), 0.02f, color, true);
                handle.DrawCircle(matrix.Transform(x4), 0.02f, color, true);
            }
        }
    }
}
