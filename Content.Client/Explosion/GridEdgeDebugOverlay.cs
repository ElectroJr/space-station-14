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


    /// <summary>
    ///     AAAAAAAAAAA
    /// </summary>
    public struct GridEdgeData : IEquatable<GridEdgeData>
    {
        public Vector2i Tile;
        public GridId Grid;
        public Box2Rotated Box;

        public GridEdgeData(Vector2i tile, GridId grid, Vector2 center, Angle angle, float size)
        {
            Tile = tile;
            Grid = grid;
            Box = new(Box2.CenteredAround(center, (size, size)), angle, center);
        }

        /// <inheritdoc />
        public bool Equals(GridEdgeData other)
        {
            return Tile.Equals(other.Tile) && Grid.Equals(other.Grid);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Tile.GetHashCode() * 397) ^ Grid.GetHashCode();
            }
        }
    }

    [UsedImplicitly]
    public sealed class GridEdgeDebugOverlay : Overlay
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        public Dictionary<GridId, HashSet<Vector2i>> GridEdges = new();
        public Dictionary<GridId, HashSet<Vector2i>> DiagGridEdges = new();
        public GridId Reference;

        private HashSet<Vector2i> _blockedNS = new(), _blockedEW = new();

        Dictionary<Vector2i, HashSet<GridEdgeData>> _transformedEdges = new();

        public const bool DrawLocalEdges = false;

        public GridEdgeDebugOverlay()
        {
            IoCManager.InjectDependencies(this);
        }

        public void Update()
        {
            _transformedEdges.Clear();
            TransformAllGridEdges();

            float x = _mapManager.GetGrid(Reference).TileSize;

            var stop = new Stopwatch();
            stop.Start();
            GetUnblockedDirections(_transformedEdges, x);
            Logger.Info($"unblock: {stop.Elapsed.TotalMilliseconds}ms");
        }

        /// <summary>
        ///     Take our map of grid edges, where each is defined in their own grid's reference frame, and map those
        ///     edges all onto one grids reference frame.
        /// </summary>
        private void TransformAllGridEdges()
        {

            if (!_mapManager.TryGetGrid(Reference, out var targetGrid))
                return;

            if (!_entityManager.TryGetComponent(targetGrid.GridEntityId, out TransformComponent xform))
                return;

            foreach (var (grid, edges) in GridEdges)
            {
                // explosion todo
                // obviously dont include the target here.
                // but for comparing performance with saltern & old code, keeping this here.

                // if (grid == target)
                //    continue;

                TransformGridEdges(grid, edges, targetGrid, xform);
            }

            foreach (var (grid, edges) in DiagGridEdges)
            {
                // explosion todo
                // obviously dont include the target here.
                // but for comparing performance with saltern & old code, keeping this here.

                // if (grid == target)
                //    continue;

                TransformDiagGridEdges(grid, edges, targetGrid, xform);
            }
        }

        /// <summary>
        ///     This is function maps the edges of a single grid onto some other grid. Used by <see
        ///     cref="TransformAllGridEdges"/>
        /// </summary>
        private void TransformGridEdges(GridId source, HashSet<Vector2i> edges, IMapGrid target, TransformComponent xform)
        {
            if (!_mapManager.TryGetGrid(source, out var sourceGrid) ||
                sourceGrid.ParentMapId != target.ParentMapId ||
                !_entityManager.TryGetComponent(sourceGrid.GridEntityId, out TransformComponent sourceTransform))
            {
                return;
            }

            if (sourceGrid.TileSize != target.TileSize)
            {
                Logger.Error($"Explosions do not support grids with different grid sizes. GridIds: {source} and {target}");
                return;
            }
            var size = (float) sourceGrid.TileSize;

            var offset = Matrix3.Identity;
            offset.R0C2 = size / 2;
            offset.R1C2 = size / 2;

            var angle = sourceTransform.WorldRotation - xform.WorldRotation;
            var matrix = offset * sourceTransform.WorldMatrix * xform.InvWorldMatrix;
            var (x, y) = angle.RotateVec((size / 4, size / 4));

            HashSet<Vector2i> transformedTiles = new();
            foreach (var tile in edges)
            {
                transformedTiles.Clear();
                var center = matrix.Transform(tile);
                TryAddEdgeTile(tile, center, x, y); // direction 1
                TryAddEdgeTile(tile, center, -y, x); // rotated 90 degrees
                TryAddEdgeTile(tile, center, -x, -y); // rotated 180 degrees
                TryAddEdgeTile(tile, center, y, -x); // rotated 279 degrees
            }

            void TryAddEdgeTile(Vector2i original, Vector2 center, float x, float y)
            {
                Vector2i newIndices = new((int) MathF.Floor(center.X + x), (int) MathF.Floor(center.Y + y));
                if (transformedTiles.Add(newIndices))
                {
                    if (!_transformedEdges.TryGetValue(newIndices, out var set))
                    {
                        set = new();
                        _transformedEdges[newIndices] = set;
                    }
                    set.Add(new(original, source, center, angle, size));
                }
            }
        }

        /// <summary>
        ///     This is a variation of <see cref="TransformGridEdges"/> and is used by <see
        ///     cref="TransformAllGridEdges"/>. This variation simply transforms the center of a tile, rather than 4
        ///     nodes.
        /// </summary>
        private void TransformDiagGridEdges(GridId source, HashSet<Vector2i> edges, IMapGrid target, TransformComponent xform)
        {
            if (!_mapManager.TryGetGrid(source, out var sourceGrid) ||
                sourceGrid.ParentMapId != target.ParentMapId ||
                !_entityManager.TryGetComponent(sourceGrid.GridEntityId, out TransformComponent sourceTransform))
            {
                return;
            }

            if (sourceGrid.TileSize != target.TileSize)
            {
                Logger.Error($"Explosions do not support grids with different grid sizes. GridIds: {source} and {target}");
                return;
            }


            var size = (float) sourceGrid.TileSize;
            var offset = Matrix3.Identity;
            offset.R0C2 = size / 2;
            offset.R1C2 = size / 2;
            var angle = sourceTransform.WorldRotation - xform.WorldRotation;
            var matrix = offset * sourceTransform.WorldMatrix * xform.InvWorldMatrix;

            foreach (var tile in edges)
            {
                var center = matrix.Transform(tile);
                Vector2i newIndices = new((int) MathF.Floor(center.X), (int) MathF.Floor(center.Y));
                if (!_transformedEdges.TryGetValue(newIndices, out var set))
                {
                    set = new();
                    _transformedEdges[newIndices] = set;
                }
                set.Add(new(tile, source, center, angle, size));
            }

        }

        /// <summary>
        ///     Given an grid-edge blocking map, check if the blockers are allowed to propagate to each other through gaps.
        /// </summary>
        /// <remarks>
        ///     After grid edges were transformed into the reference frame of some other grid, this function figures out
        ///     which of those edges are actually blocking explosion propagation.
        /// </remarks>
        private void GetUnblockedDirections(Dictionary<Vector2i, HashSet<GridEdgeData>> transformedEdges, float tileSize)
        {
            _blockedNS = new();
            _blockedEW = new();

            foreach (var (tile, data) in transformedEdges)
            {
                foreach (var datum in data)
                {
                    var tileCenter = ((Vector2) tile + 0.5f) * tileSize;
                    if (datum.Box.Contains(tileCenter))
                    {
                        _blockedNS.Add(tile);
                        _blockedEW.Add(tile);
                        _blockedNS.Add(tile + (0, -1));
                        _blockedEW.Add(tile + (-1, 0));
                        break;
                    }

                    // check north
                    if (datum.Box.Contains(tileCenter + (0, tileSize / 2)))
                    {
                        _blockedNS.Add(tile);
                    }

                    // check south
                    if (datum.Box.Contains(tileCenter + (0, -tileSize / 2)))
                    {
                        _blockedNS.Add(tile + (0, -1));
                    }

                    // check east
                    if (datum.Box.Contains(tileCenter + (tileSize / 2, 0)))
                    {
                        _blockedEW.Add(tile);
                    }

                    // check west
                    if (datum.Box.Contains(tileCenter + (-tileSize / 2, 0)))
                    {
                        _blockedEW.Add(tile + (-1, 0));
                    }
                }
            }
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            var worldBounds = _eyeManager.GetWorldViewbounds();
            var handle = args.WorldHandle;

            if (!_mapManager.TryGetGrid(Reference, out var referenceGrid))
                return;

            if (DrawLocalEdges)
            {
                foreach (var (gridId, edges) in GridEdges)
                {
                    if (!_mapManager.TryGetGrid(gridId, out var grid))
                        continue;

                    if (grid.ParentMapId != _eyeManager.CurrentMap)
                        continue;

                    DrawEdges(grid, edges, handle, worldBounds, Color.Yellow);
                    DrawNode(grid, edges, handle, worldBounds, Color.Yellow);
                }
            }

            Update();
            DrawBlockingEdges(referenceGrid, handle, worldBounds);
        }

        private void DrawBlockingEdges(IMapGrid grid, DrawingHandleWorld handle, Box2Rotated worldBounds)
        {
            var gridXform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
            var gridBounds = gridXform.InvWorldMatrix.TransformBox(worldBounds);
            var worldRot = gridXform.WorldRotation;
            var matrix = gridXform.WorldMatrix;
            handle.SetTransform(matrix);

            foreach (var tile in _transformedEdges.Keys)
            {
                // is the center of this tile visible to the user?
                var tileCenter = (Vector2) tile + 0.5f;
                if (!gridBounds.Contains(tileCenter))
                    continue;

                Vector2 SW = tile;
                var NW = SW + (0, 1);
                var SE = SW + (1, 0);
                var NE = SW + (1, 1);

                DrawThickLine(handle, NW, NE, _blockedNS.Contains(tile) ? Color.Red : Color.Green);
                DrawThickLine(handle, SW, SE, _blockedNS.Contains(tile + (0, -1)) ? Color.Red : Color.Green);
                DrawThickLine(handle, SE, NE, _blockedEW.Contains(tile) ? Color.Red : Color.Green);
                DrawThickLine(handle, SW, NW, _blockedEW.Contains(tile + (-1, 0)) ? Color.Red : Color.Green);
            }
        }

        private void DrawThickLine(DrawingHandleWorld handle, Vector2 start, Vector2 end, Color color, float thickness = 0.025f)
        {
            Box2 box = new(start, end);
            handle.DrawRect(box.Enlarged(thickness), color);
        }

        private void DrawEdges(IMapGrid grid, IEnumerable<Vector2i> edges, DrawingHandleWorld handle, Box2Rotated worldBounds, Color color)
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
