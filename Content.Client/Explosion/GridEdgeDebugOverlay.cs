using Content.Shared.Atmos;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;
using System.Linq;
using static Content.Shared.Atmos.AtmosDirectionHelpers;

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

        private Dictionary<Vector2i, bool> _knownNS = new(), _knownEW = new();

        private readonly SharedPhysicsSystem _sharedPhysicsSystem;

        private HashSet<Vector2i> _transformedEdges = new();

        public static readonly Matrix3 Offset = new(
            1, 0, 0.25f,
            0, 1, 0.25f,
            0, 0, 1
        );

        public GridEdgeDebugOverlay()
        {
            IoCManager.InjectDependencies(this);
            _sharedPhysicsSystem = EntitySystem.Get<SharedPhysicsSystem>();
        }

        public void Update()
        {
            _transformedEdges = new();

            foreach (var (gridId, edges) in GridEdges)
            {
                _transformedEdges.UnionWith(TransformGridEdge(edges, gridId, Reference));
            }

            var stop = new Stopwatch();
            stop.Start();
            GetUnblockedDirections();
            Logger.Info($"unblock: {stop.Elapsed.TotalMilliseconds}ms");
        }

        /// <summary>
        ///     Given an grid-edge blocking map, check if the blockers are allowed to propagate to each other through gaps.
        /// </summary>
        private void GetUnblockedDirections()
        {
            _knownNS = new();
            _knownEW = new();

            if (!_mapManager.TryGetGrid(Reference, out var grid))
                return;

            var worldMatrix = grid.WorldMatrix;
            var rot = grid.WorldRotation;

            foreach (var tile in _transformedEdges)
            {
                GetUnblockedDirections(grid.ParentMapId, grid.TileSize, worldMatrix, rot, tile);
            }
        }

        /// <summary>
        ///     Given an grid-edge blocking map, check if the blockers are allowed to propagate to each other through gaps.
        /// </summary>
        private void GetUnblockedDirections(MapId mapId, float tileSize, Matrix3 worldMatrix, Angle rotation, Vector2i index)
        {
            var pos = worldMatrix.Transform(new Vector2((index.X + 0.5f) * tileSize, (index.Y + 0.5f) * tileSize));
            var offset = rotation.RotateVec(new Vector2(0, 1));
            var offset2 = offset.Rotated90DegreesClockwiseWorld;

            // Check north
            if (!_knownNS.TryGetValue(index, out var blocked))
            {
                var ray = new CollisionRay(pos, offset, MapGridHelpers.CollisionGroup);
                blocked = _sharedPhysicsSystem.IntersectRayWithPredicate(mapId, ray, tileSize, returnOnFirstHit: true).Any();
                _knownNS[index] = blocked;
            }

            // Check south
            if (!_knownNS.TryGetValue(index + (0, -1), out blocked))
            {
                var ray = new CollisionRay(pos, -offset, MapGridHelpers.CollisionGroup);
                blocked = _sharedPhysicsSystem.IntersectRayWithPredicate(mapId, ray, tileSize, returnOnFirstHit: true).Any();
                _knownNS[index + (0, -1)] = blocked;
            }

            // Check east
            if (!_knownEW.TryGetValue(index, out blocked))
            {
                var ray = new CollisionRay(pos, offset2, MapGridHelpers.CollisionGroup);
                blocked = _sharedPhysicsSystem.IntersectRayWithPredicate(mapId, ray, tileSize, returnOnFirstHit: true).Any();
                _knownEW[index] = blocked;
            }

            // Check West
            if (!_knownEW.TryGetValue(index + (-1, 0), out blocked))
            {
                var ray = new CollisionRay(pos, -offset2, MapGridHelpers.CollisionGroup);
                blocked = _sharedPhysicsSystem.IntersectRayWithPredicate(mapId, ray, tileSize, returnOnFirstHit: true).Any();
                _knownEW[index + (-1, 0)] = blocked;
            }
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
            var offset2 = offset1.Rotated90DegreesClockwiseWorld;

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
            }


            Update();
            DrawEdges(referenceGrid, _transformedEdges, handle, worldBounds, Color.Red);
            DrawUnblockedEdges(referenceGrid, handle, worldBounds, Color.Green);
        }

        private void DrawUnblockedEdges(IMapGrid grid, DrawingHandleWorld handle, Box2Rotated worldBounds, Color color)
        {
            var gridXform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
            var gridBounds = gridXform.InvWorldMatrix.TransformBox(worldBounds);
            var worldRot = gridXform.WorldRotation;
            var matrix = gridXform.WorldMatrix;

            foreach (var tile in _transformedEdges)
            {
                // is the center of this tile visible to the user?
                var tileCenter = (Vector2) tile + 0.5f;
                if (!gridBounds.Contains(tileCenter))
                    continue;

                var mapCenter = matrix.Transform(tileCenter);

                var offsetNorth = worldRot.RotateVec((0, grid.TileSize/2f));
                var offsetEast = offsetNorth.Rotated90DegreesClockwiseWorld;

                if (!_knownNS[tile])
                    handle.DrawLine(mapCenter, mapCenter + offsetNorth, color);
                if (!_knownNS[tile + (0, -1)])
                    handle.DrawLine(mapCenter, mapCenter - offsetNorth, color);
                if (!_knownEW[tile])
                    handle.DrawLine(mapCenter, mapCenter + offsetEast, color);
                if (!_knownEW[tile + (-1, 0)])
                    handle.DrawLine(mapCenter, mapCenter - offsetEast, color);
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
