using System;
using System.Collections.Generic;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    // in order to check if tiles are unblocked
    // just check if the rotated box contains the center and the half way point

    // actually if the center is ever inside another grid.
    // can mark that as a "true block"
    // then only ever need to test halfway point


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

    // This partial part of the explosion system has all of the functions used to facilitate explosions moving across grids.
    // A good portion of it is focused around keeping track of what tile-indices on a grid correspond to tiles that border space.
    // AFAIK no other system needs to track these "edge-tiles". If they do, this should probably be a property of the grid itself?
    public sealed partial class ExplosionSystem : EntitySystem
    {
        /// <summary>
        ///     Set of tiles of each grid that are directly adjacent to space
        /// </summary>
        private Dictionary<GridId, HashSet<Vector2i>> _gridEdges = new();

        /// <summary>
        ///     Set of tiles of each grid that are diagonally adjacent to space
        /// </summary>
        private Dictionary<GridId, HashSet<Vector2i>> _diagGridEdges = new();

        public void SendEdges(GridId referenceGrid)
        {
            // temporary for debugging.
            // todo remove

            RaiseNetworkEvent(new GridEdgeUpdateEvent(referenceGrid, _gridEdges, _diagGridEdges));
        }

        /// <summary>
        ///     On grid startup, prepare a map of grid edges.
        /// </summary>
        /// <param name="ev"></param>
        private void OnGridStartup(GridStartupEvent ev)
        {
            if (!_mapManager.TryGetGrid(ev.GridId, out var grid))
                return;

            HashSet<Vector2i> edges = new(), diagEdges = new();
            _gridEdges[ev.GridId] = edges;
            _diagGridEdges[ev.GridId] = diagEdges;

            foreach (var tileRef in grid.GetAllTiles())
            {
                if (tileRef.Tile.IsEmpty)
                    continue;

                if (IsEdge(grid, tileRef.GridIndices))
                    edges.Add(tileRef.GridIndices);
                else if (IsDiagonalEdge(grid, tileRef.GridIndices))
                    diagEdges.Add(tileRef.GridIndices);
            }
        }

        private void OnGridRemoved(GridRemovalEvent ev)
        {
            _airtightMap.Remove(ev.GridId);
            _gridEdges.Remove(ev.GridId);
            _diagGridEdges.Remove(ev.GridId);
        }

        /// <summary>
        ///     Take our map of grid edges, where each is defined in their own grid's reference frame, and map those
        ///     edges all onto one grids reference frame.
        /// </summary>
        private Dictionary<Vector2i, HashSet<GridEdgeData>> TransformAllGridEdges(GridId target)
        {
            Dictionary<Vector2i, HashSet<GridEdgeData>> transformedEdges = new();

            if (!_mapManager.TryGetGrid(target, out var targetGrid))
                return transformedEdges;

            if (!EntityManager.TryGetComponent(targetGrid.GridEntityId, out TransformComponent xform))
                return transformedEdges;

            foreach (var (grid, edges) in _gridEdges)
            {
                // explosion todo
                // obviously dont include the target here.
                // but for comparing performance with saltern & old code, keeping this here.

                // if (grid == target)
                //    continue;

                TransformGridEdges(grid, edges, targetGrid, xform, transformedEdges);
            }

            foreach (var (grid, edges) in _diagGridEdges)
            {
                // explosion todo
                // obviously dont include the target here.
                // but for comparing performance with saltern & old code, keeping this here.

                // if (grid == target)
                //    continue;

                TransformDiagGridEdges(grid, edges, targetGrid, xform, transformedEdges);
            }

            return transformedEdges;
        }

        /// <summary>
        ///     This is function maps the edges of a single grid onto some other grid. Used by <see
        ///     cref="TransformAllGridEdges"/>
        /// </summary>
        private void TransformGridEdges(GridId source, HashSet<Vector2i> edges, IMapGrid target, TransformComponent xform, Dictionary<Vector2i, HashSet<GridEdgeData>> transformedEdges)
        {
            if (!_mapManager.TryGetGrid(source, out var sourceGrid) ||
                sourceGrid.ParentMapId != target.ParentMapId ||
                !EntityManager.TryGetComponent(sourceGrid.GridEntityId, out TransformComponent sourceTransform))
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
                    if (!transformedEdges.TryGetValue(newIndices, out var set))
                    {
                        set = new();
                        transformedEdges[newIndices] = set;
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
        private void TransformDiagGridEdges(GridId source, HashSet<Vector2i> edges, IMapGrid target, TransformComponent xform, Dictionary<Vector2i, HashSet<GridEdgeData>> transformedEdges)
        {
            if (!_mapManager.TryGetGrid(source, out var sourceGrid) ||
                sourceGrid.ParentMapId != target.ParentMapId ||
                !EntityManager.TryGetComponent(sourceGrid.GridEntityId, out TransformComponent sourceTransform))
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
                if (!transformedEdges.TryGetValue(newIndices, out var set))
                {
                    set = new();
                    transformedEdges[newIndices] = set;
                }

                // explosions are not allowed to propagate diagonally ONTO grids.
                // so we invalidate the grid id.
                set.Add(new(tile, GridId.Invalid, center, angle, size));
            }
        }

        /// <summary>
        ///     Given an grid-edge blocking map, check if the blockers are allowed to propagate to each other through gaps.
        /// </summary>
        /// <remarks>
        ///     After grid edges were transformed into the reference frame of some other grid, this function figures out
        ///     which of those edges are actually blocking explosion propagation.
        /// </remarks>
        private (HashSet<Vector2i>, HashSet<Vector2i>) GetUnblockedDirections(Dictionary<Vector2i, HashSet<GridEdgeData>> transformedEdges, float tileSize)
        {
            HashSet<Vector2i> blockedNS = new(), blockedEW = new();

            foreach (var (tile, data) in transformedEdges)
            {
                foreach (var datum in data)
                {
                    var tileCenter = ((Vector2) tile + 0.5f) * tileSize;
                    if (datum.Box.Contains(tileCenter))
                    {
                        blockedNS.Add(tile);
                        blockedEW.Add(tile);
                        blockedNS.Add(tile + (0, -1));
                        blockedEW.Add(tile + (-1, 0));
                        break;
                    }

                    // its faster to just check Box.Contains, instead of first checking Hashset.Contains().

                    // check north
                    if (datum.Box.Contains(tileCenter + (0, tileSize / 2)))
                    {
                        blockedNS.Add(tile);
                    }

                    // check south
                    if (datum.Box.Contains(tileCenter + (0, -tileSize / 2)))
                    {
                        blockedNS.Add(tile + (0, -1));
                    }

                    // check east
                    if (datum.Box.Contains(tileCenter + (tileSize / 2, 0)))
                    {
                        blockedEW.Add(tile);
                    }

                    // check south
                    if (datum.Box.Contains(tileCenter + (-tileSize / 2, 0)))
                    {
                        blockedEW.Add(tile + (-1, 0));
                    }
                }
            }
            return (blockedNS, blockedEW);
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

            if (!_diagGridEdges.TryGetValue(tileRef.GridIndex, out var diagEdges))
            {
                diagEdges = new();
                _diagGridEdges[tileRef.GridIndex] = diagEdges;
            }

            if (tileRef.Tile.IsEmpty)
            {
                // if the tile is empty, it cannot itself be an edge tile.
                edges.Remove(tileRef.GridIndices);
                diagEdges.Remove(tileRef.GridIndices);

                // add any valid neighbours to the list of edge-tiles
                foreach (var neighborIndex in GetCardinalNeighbors(tileRef.GridIndices))
                {
                    if (grid.TryGetTileRef(neighborIndex, out var neighborTile) && !neighborTile.Tile.IsEmpty)
                    {
                        edges.Add(neighborIndex);
                        diagEdges.Remove(neighborIndex);
                    }
                }

                foreach (var neighborIndex in GetDiagonalNeighbors(tileRef.GridIndices))
                {
                    if (edges.Contains(neighborIndex))
                        continue;

                    if (grid.TryGetTileRef(neighborIndex, out var neighborTile) && !neighborTile.Tile.IsEmpty)
                        diagEdges.Add(neighborIndex);
                }

                return;
            }

            // this tile is not empty space, but may previously have been. If any of its neighbours are edge tiles,
            // check that they still border space in some other direction.
            foreach (var neighborIndex in GetCardinalNeighbors(tileRef.GridIndices))
            {
                if (edges.Contains(neighborIndex) && !IsEdge(grid, neighborIndex, tileRef.GridIndices))
                {
                    // no longer a direct edge ...
                    edges.Remove(neighborIndex);

                    // ... but it could now be a diagonal edge
                    if (IsDiagonalEdge(grid, neighborIndex, tileRef.GridIndices))
                        diagEdges.Add(neighborIndex);
                }
            }

            // and again for diagonal neighbours
            foreach (var neighborIndex in GetDiagonalNeighbors(tileRef.GridIndices))
            {
                if (diagEdges.Contains(neighborIndex) && !IsDiagonalEdge(grid, neighborIndex, tileRef.GridIndices))
                    diagEdges.Remove(neighborIndex);
            }

            // finally check if the new tile is itself an edge tile
            if (IsEdge(grid, tileRef.GridIndices))
                edges.Add(tileRef.GridIndices);
            else if (IsDiagonalEdge(grid, tileRef.GridIndices))
                diagEdges.Add(tileRef.GridIndices);
        }

        /// <summary>
        ///     Check whether a tile is on the edge of a grid (i.e., whether it borders space).
        /// </summary>
        /// <remarks>
        ///     Optionally ignore a specific Vector2i. Used by <see cref="OnTileChanged"/> when we already know that a
        ///     given tile is not space. This avoids unnecessary TryGetTileRef calls.
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

        private bool IsDiagonalEdge(IMapGrid grid, Vector2i index, Vector2i? ignore = null)
        {
            foreach (var neighbourIndex in GetDiagonalNeighbors(index))
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

        /// <summary>
        ///     Enumerate over diagonally adjacent tiles.
        /// </summary>
        private static IEnumerable<Vector2i> GetDiagonalNeighbors(Vector2i pos)
        {
            yield return pos + (1, 1);
            yield return pos + (-1, -1);
            yield return pos + (1, -1);
            yield return pos + (-1, 1);
        }
    }
}
