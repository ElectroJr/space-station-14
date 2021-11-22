using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;

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
        [Dependency] private readonly SharedPhysicsSystem _sharedPhysicsSystem = default!;

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
            var offset1 = angle.RotateVec((0, size/2));
            var offset2 = offset1.Rotated90DegreesClockwiseWorld;

            HashSet<Vector2i> transformedTiles = new();
            foreach (var tile in edges)
            {
                transformedTiles.Clear();
                var transformed = matrix.Transform(tile);
                TryAddEdgeTile(tile, transformed);
                transformed += offset1;
                TryAddEdgeTile(tile, transformed);
                transformed += offset2;
                TryAddEdgeTile(tile, transformed);
                transformed -= offset1;
                TryAddEdgeTile(tile, transformed);
            }

            void TryAddEdgeTile(Vector2i original, Vector2 transformed)
            {
                Vector2i newIndices = new((int) MathF.Floor(transformed.X), (int) MathF.Floor(transformed.Y));
                if (transformedTiles.Add(newIndices))
                {
                    if (!transformedEdges.TryGetValue(newIndices, out var set))
                    {
                        set = new();
                        transformedEdges[newIndices] = set;
                    }
                    set.Add(new(original, source, transformed, angle, size));
                }
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
                    var tileCenter = (Vector2) tile + tileSize;
                    if (datum.Box.Contains(tileCenter))
                    {
                        blockedNS.Add(tile);
                        blockedEW.Add(tile);
                        blockedNS.Add(tile + (0, -1));
                        blockedEW.Add(tile + (-1, 0));
                        break;
                    }

                    // EXPLOSIONS TODO PERORMANCE
                    // what is faster:
                    // checking if blockedNS contains tile, and THEN checking for intersections?
                    // OR just checking for intersections, and then adding it anyways?
                    //
                    // for the latter.... will still check if it contains it when it actually does the adding right?
                    // but when it's NOT blocked (intersection fails), then it will never have to check? but I imagine 90% of the time it WILL be blocked.

                    // check north
                    if (!blockedNS.Contains(tile) && datum.Box.Contains(tileCenter + (0, tileSize / 2)))
                    {
                        blockedNS.Add(tile);
                    }

                    // check south
                    if (!blockedNS.Contains(tile + (0, -1)) && datum.Box.Contains(tileCenter + (0, -tileSize / 2)))
                    {
                        blockedNS.Add(tile + (0, -1));
                    }

                    // check east
                    if (!blockedEW.Contains(tile) && datum.Box.Contains(tileCenter + (tileSize / 2, 0)))
                    {
                        blockedEW.Add(tile);
                    }

                    // check south
                    if (!blockedEW.Contains(tile + (-1, 0)) && datum.Box.Contains(tileCenter + (-tileSize / 2, 0)))
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
