using System;
using System.Collections.Generic;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    // This partial part of the explosion system has all of the functions used to facilitate explosions moving across grids.
    // A good portion of it is focused around keeping track of what tile-indices on a grid correspond to tiles that border space.
    // AFAIK no other system needs to track these "edge-tiles". If they do, this should probably be a property of the grid itself?
    public sealed partial class ExplosionSystem : EntitySystem
    {
        public static readonly Matrix3 Offset = new(
            1, 0, 0.25f,
            0, 1, 0.25f,
            0, 0, 1
        );

        /// <summary>
        ///     Set of tiles of each grid that are directly adjacent to space
        /// </summary>
        private Dictionary<GridId, HashSet<Vector2i>> _gridEdges = new();

        public void SendEdges(GridId referenceGrid)
        {
            // temporary for debugging.
            // todo remove
            RaiseNetworkEvent(new GridEdgeUpdateEvent(referenceGrid, _gridEdges));
        }

        /// <summary>
        ///     On grid startup, prepare a map of grid edges.
        /// </summary>
        /// <param name="ev"></param>
        private void OnGridStartup(GridStartupEvent ev)
        {
            if (!_mapManager.TryGetGrid(ev.GridId, out var grid))
                return;

            HashSet<Vector2i> edges = new();
            _gridEdges.Add(ev.GridId, edges);

            foreach (var tileRef in grid.GetAllTiles())
            {
                if (tileRef.Tile.IsEmpty)
                    continue;

                if (IsEdge(grid, tileRef.GridIndices))
                    edges.Add(tileRef.GridIndices);
            }
        }

        private void OnGridRemoved(GridRemovalEvent ev)
        {
            _airtightMap.Remove(ev.GridId);
            _gridEdges.Remove(ev.GridId);
        }

        /// <summary>
        ///     Take the set of edges for some grid, and map them into Vector2i indices for some other grid. This
        ///     ASSUMES that the two grids have the same grid size.
        /// </summary>
        /// <remarks>
        ///     IF both grids have the same grid size, then grid-indices map 1:1, regardless of how the grids are
        ///     translated or rotated. Additionally, the non-empty spaces of the grids should never overlap.
        /// </remarks>
        private HashSet<Vector2i> TransformGridEdge(GridId source, GridId target)
        {
            if (!_gridEdges.TryGetValue(source, out var edges))
                return new();

            if (source == target)
                return edges;

            HashSet<Vector2i> targetEdges = new();

            if (!_mapManager.TryGetGrid(source, out var sourceGrid) ||
                !_mapManager.TryGetGrid(target, out var targetGrid) ||
                !EntityManager.TryGetComponent(sourceGrid.GridEntityId, out TransformComponent sourceTransform) ||
                !EntityManager.TryGetComponent(targetGrid.GridEntityId, out TransformComponent targetTransform))
            {
                return targetEdges;
            }

            if (sourceGrid.TileSize != targetGrid.TileSize)
            {
                Logger.Error($"Explosions do not support grids with different grid sizes. GridIds: {source} and {target}");
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

        /// <summary>
        ///     When a tile is updated, we might need to update the grid edge maps.
        /// </summary>
        private void MapManagerOnTileChanged(object? sender, TileChangedEventArgs e)
        {
            // only need to update the grid-edge map if the tile changed from space to not-space.
            if (e.NewTile.Tile.TypeId != e.OldTile.TypeId)
                OnTileChanged(e.NewTile);
        }

        private void OnTileChanged(TileRef tileRef)
        {
            if (!_mapManager.TryGetGrid(tileRef.GridIndex, out var grid))
                return;

            if (!_gridEdges.TryGetValue(tileRef.GridIndex, out var edges))
            {
                edges = new();
                _gridEdges[tileRef.GridIndex] = edges;
            }

            if (tileRef.Tile.IsEmpty)
            {
                // add any valid neighbours to the list of edge-tiles
                foreach (var neighborIndex in GetCardinalNeighbors(tileRef.GridIndices))
                {
                    if (grid.TryGetTileRef(neighborIndex, out var neighborTile) && !neighborTile.Tile.IsEmpty)
                        edges.Add(neighborIndex);
                }

                // if the tile is empty, it cannot itself be an edge tile.
                edges.Remove(tileRef.GridIndices);

                return;
            }

            // this tile is not empty space, but may previously have been. If any of its neighbours are edge tiles,
            // check that they still border space in some other direction.
            foreach (var neighborIndex in GetCardinalNeighbors(tileRef.GridIndices))
            {
                if (edges.Contains(neighborIndex) && !IsEdge(grid, neighborIndex, tileRef.GridIndices))
                    edges.Remove(neighborIndex);
            }

            // finally check if the new tile is itself an edge tile
            if (IsEdge(grid, tileRef.GridIndices))
                edges.Add(tileRef.GridIndices);
        }

        /// <summary>
        ///     Check whether a tile is on the edge of a grid (i.e., whether it borders space).
        /// </summary>
        /// <remarks>
        ///     Optionally ignore a specific Vector2i. Used by <see cref="OnTileChanged"/> when we already know that a
        ///     given tile is not space. This avoids uneccesary TryGetTileRef calls.
        /// </remarks>
        private bool IsEdge(IMapGrid grid, Vector2i index, Vector2i? ignore = null)
        {
            foreach (var neighbourIndex in GetCardinalNeighbors(index))
            {
                if (neighbourIndex == ignore)
                    continue;

                if (!grid.TryGetTileRef(neighbourIndex, out var neighborTile) || neighborTile.Tile.IsEmpty)
                    return true;
            }

            return false;
        }

        // Is this really not an existing function somewhere?
        // I guess it doesn't belong in robust.math? but somewhere, surely?
        /// <summary>
        ///     Enumerate over directly adjacent tiles.
        /// </summary>
        private static IEnumerable<Vector2i> GetCardinalNeighbors(Vector2i pos)
        {
            yield return pos + (1, 0);
            yield return pos + (0, 1);
            yield return pos + (-1, 0);
            yield return pos + (0, -1);
        }
    }
}
