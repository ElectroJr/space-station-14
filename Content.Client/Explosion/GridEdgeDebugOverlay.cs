using Content.Shared.Atmos;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;

namespace Content.Client.Explosion;

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


/// <summary>
///     BBBBBBBBBB
/// </summary>
public record GridBlockData
{
    /// <summary>
    ///     What directions of this tile are not blocked by some other grid?
    /// </summary>
    public AtmosDirection UnblockedDirections = AtmosDirection.All;

    /// <summary>
    ///     Hashset contains information about the edge-tiles, which belong to some other grid(s), that are blocking
    ///     this tile.
    /// </summary>
    public HashSet<GridEdgeData> BlockingGridEdges = new();
}

[UsedImplicitly]
public sealed class GridEdgeDebugOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    public const float DefaultTileSize = 1;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public Dictionary<GridId, Dictionary<Vector2i, AtmosDirection>> _gridEdges = new();
    public Dictionary<GridId, HashSet<Vector2i>> _diagGridEdges = new();
    public GridId Reference;

    Dictionary<Vector2i, GridBlockData> _transformedEdges = new();

    public const bool DrawLocalEdges = false;

    public GridEdgeDebugOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    public void Update()
    {
        _transformedEdges.Clear();

        _mapManager.TryGetGrid(Reference, out var grid);
        var map = grid?.ParentMapId ?? _eyeManager.CurrentMap;

        var stop = new Stopwatch();
        stop.Start();

        _transformedEdges = TransformGridEdges(map, Reference);
        GetUnblockedDirections(_transformedEdges, 1f);

        Logger.Info($"unblock: {stop.Elapsed.TotalMilliseconds}ms");
    }

    /// <summary>
    ///     Take our map of grid edges, where each is defined in their own grid's reference frame, and map those
    ///     edges all onto one grids reference frame.
    /// </summary>
    public Dictionary<Vector2i, GridBlockData> TransformGridEdges(MapId targetMap, GridId referenceGrid)
    {
        Dictionary<Vector2i, GridBlockData> transformedEdges = new();

        var targetMatrix = Matrix3.Identity;
        Angle targetAngle = new();
        float tileSize = DefaultTileSize;

        // if the explosion is centered on some grid (and not just space), get the transforms.
        if (referenceGrid.IsValid())
        {
            var targetGrid = _mapManager.GetGrid(referenceGrid);
            var xform = _entityManager.GetComponent<TransformComponent>(targetGrid.GridEntityId);
            targetAngle = xform.WorldRotation;
            targetMatrix = xform.InvWorldMatrix;
            tileSize = targetGrid.TileSize;
        }

        var offsetMatrix = Matrix3.Identity;
        offsetMatrix.R0C2 = tileSize / 2;
        offsetMatrix.R1C2 = tileSize / 2;

        // here we will get a triple nested for loop:
        // foreach other grid
        //   foreach edge tile in that grid
        //     foreach tile in our grid that touches that tile

        HashSet<Vector2i> transformedTiles = new();
        foreach (var (gridToTransform, edges) in _gridEdges)
        {
            // we treat the target grid separately
            if (gridToTransform == referenceGrid)
                continue;

            if (!_mapManager.TryGetGrid(gridToTransform, out var grid) ||
                grid.ParentMapId != targetMap)
                continue;

            if (grid.TileSize != tileSize)
            {
                Logger.Error($"Explosions do not support grids with different grid sizes. GridIds: {gridToTransform} and {referenceGrid}");
                continue;
            }

            var xform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
            var matrix = offsetMatrix * xform.WorldMatrix * targetMatrix;
            var angle = xform.WorldRotation - targetAngle;

            var (x, y) = angle.RotateVec((tileSize / 4, tileSize / 4));

            foreach (var (tile, dir) in edges)
            {
                var center = matrix.Transform(tile);

                // this tile might touch several other tiles, or maybe just one tile. Here we use a Vector2i HashSet to
                // remove duplicates.
                transformedTiles.Clear();
                transformedTiles.Add(new((int) MathF.Floor(center.X + x), (int) MathF.Floor(center.Y + y)));  // initial direction
                transformedTiles.Add(new((int) MathF.Floor(center.X - y), (int) MathF.Floor(center.Y + x)));  // rotated 90 degrees
                transformedTiles.Add(new((int) MathF.Floor(center.X - x), (int) MathF.Floor(center.Y - y)));  // rotated 180 degrees
                transformedTiles.Add(new((int) MathF.Floor(center.X + y), (int) MathF.Floor(center.Y - x)));  // rotated 270 degrees

                foreach (var newIndices in transformedTiles)
                {
                    if (!transformedEdges.TryGetValue(newIndices, out var data))
                    {
                        data = new();
                        transformedEdges[newIndices] = data;
                    }
                    data.BlockingGridEdges.Add(new(tile, gridToTransform, center, angle, tileSize));
                }
            }
        }

        // Next we transform any diagonal edges.
        Vector2i newIndex;
        foreach (var (gridToTransform, diagEdges) in _diagGridEdges)
        {
            // we treat the target grid separately
            if (gridToTransform == referenceGrid)
                continue;

            if (!_mapManager.TryGetGrid(gridToTransform, out var grid) ||
                grid.ParentMapId != targetMap)
                continue;

            if (grid.TileSize != tileSize)
            {
                Logger.Error($"Explosions do not support grids with different grid sizes. GridIds: {gridToTransform} and {referenceGrid}");
                continue;
            }

            var xform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
            var matrix = offsetMatrix * xform.WorldMatrix * targetMatrix;
            var angle = xform.WorldRotation - targetAngle;

            foreach (var tile in diagEdges)
            {
                var center = matrix.Transform(tile);
                newIndex = new((int) MathF.Floor(center.X), (int) MathF.Floor(center.Y));
                if (!transformedEdges.TryGetValue(newIndex, out var data))
                {
                    data = new();
                    transformedEdges[newIndex] = data;
                }

                // explosions are not allowed to propagate diagonally ONTO grids. so we just use defaults for some fields.
                data.BlockingGridEdges.Add(new(default, default, center, angle, tileSize));
            }
        }

        // finally, we also include the blocking tiles from the reference grid (if its not space).

        if (_gridEdges.TryGetValue(referenceGrid, out var localEdges))
        {
            foreach (var (tile, _) in localEdges)
            {
                if (!transformedEdges.TryGetValue(tile, out var data))
                {
                    data = new();
                    transformedEdges[tile] = data;
                }

                data.BlockingGridEdges.Add(new(tile, referenceGrid, ((Vector2) tile + 0.5f) * tileSize, 0, tileSize));
            }
        }

        if (_diagGridEdges.TryGetValue(referenceGrid, out var localDiagEdges))
        {
            foreach (var tile in localDiagEdges)
            {
                if (!transformedEdges.TryGetValue(tile, out var data))
                {
                    data = new();
                    transformedEdges[tile] = data;
                }

                data.BlockingGridEdges.Add(new(default, default, ((Vector2) tile + 0.5f) * tileSize, 0, tileSize));
            }
        }

        return transformedEdges;
    }

    /// <summary>
    ///     Given an grid-edge blocking map, check if the blockers are allowed to propagate to each other through gaps.
    /// </summary>
    /// <remarks>
    ///     After grid edges were transformed into the reference frame of some other grid, this function figures out
    ///     which of those edges are actually blocking explosion propagation.
    /// </remarks>
    public void GetUnblockedDirections(Dictionary<Vector2i, GridBlockData> transformedEdges, float tileSize)
    {
        foreach (var (tile, data) in transformedEdges)
        {
            if (data.UnblockedDirections == AtmosDirection.Invalid)
                continue; // already all blocked.

            var tileCenter = ((Vector2) tile + 0.5f) * tileSize;
            foreach (var edge in data.BlockingGridEdges)
            {
                // if a blocking edge contains the center of the tile, block all directions
                if (edge.Box.Contains(tileCenter))
                {
                    data.UnblockedDirections = AtmosDirection.Invalid;
                    break;
                }

                // check north
                if (edge.Box.Contains(tileCenter + (0, tileSize / 2)))
                    data.UnblockedDirections &= ~AtmosDirection.North;

                // check south
                if (edge.Box.Contains(tileCenter + (0, -tileSize / 2)))
                    data.UnblockedDirections &= ~AtmosDirection.South;

                // check east
                if (edge.Box.Contains(tileCenter + (tileSize / 2, 0)))
                    data.UnblockedDirections &= ~AtmosDirection.East;

                // check west
                if (edge.Box.Contains(tileCenter + (-tileSize / 2, 0)))
                    data.UnblockedDirections &= ~AtmosDirection.West;
            }
        }
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldBounds = _eyeManager.GetWorldViewbounds();
        var handle = args.WorldHandle;

        if (DrawLocalEdges)
        {
            foreach (var (gridId, edges) in _gridEdges)
            {
                if (!_mapManager.TryGetGrid(gridId, out var grid))
                    continue;

                if (grid.ParentMapId != _eyeManager.CurrentMap)
                    continue;

                DrawEdges(grid, edges.Keys, handle, worldBounds, Color.Yellow);
                DrawNode(grid, edges.Keys, handle, worldBounds, Color.Yellow);
            }
        }

        Update();
        DrawBlockingEdges(handle, worldBounds);
    }

    private void DrawBlockingEdges(DrawingHandleWorld handle, Box2Rotated worldBounds)
    {
        var matrix = Matrix3.Identity;

        if (Reference.IsValid())
        {
            if (!_mapManager.TryGetGrid(Reference, out var grid))
                return;
            var gridXform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
            matrix = gridXform.WorldMatrix;
        }

        handle.SetTransform(matrix);

        foreach (var tile in _transformedEdges.Keys)
        {
            // is the center of this tile visible to the user?
            var tileCenter = (Vector2) tile + 0.5f;
            if (!worldBounds.Contains(matrix.Transform(tileCenter)))
                continue;

            float thickness = 0.025f;

            Vector2 SW = tile;
            var NW = SW + (0, 1);
            var SE = SW + (1, 0);
            var NE = SW + (1, 1);

            SW += (thickness, thickness);
            NE += (-thickness, -thickness);
            SE += (-thickness, thickness);
            NW += (+thickness, -thickness);

            var dirs = _transformedEdges[tile].UnblockedDirections;

            DrawThickLine(handle, NW, NE, dirs.IsFlagSet(AtmosDirection.North) ? Color.Green : Color.Red, thickness * 2 / 3);
            DrawThickLine(handle, SW, SE, dirs.IsFlagSet(AtmosDirection.South) ? Color.Green : Color.Red, thickness * 2 / 3);
            DrawThickLine(handle, SE, NE, dirs.IsFlagSet(AtmosDirection.East)  ? Color.Green : Color.Red, thickness * 2 / 3);
            DrawThickLine(handle, SW, NW, dirs.IsFlagSet(AtmosDirection.West)  ? Color.Green : Color.Red, thickness * 2 / 3);
        }
    }

    private void DrawThickLine(DrawingHandleWorld handle, Vector2 start, Vector2 end, Color color, float thickness)
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

    private void DrawNode(IMapGrid grid, IEnumerable<Vector2i> edges, DrawingHandleWorld handle, Box2Rotated worldBounds, Color color)
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
