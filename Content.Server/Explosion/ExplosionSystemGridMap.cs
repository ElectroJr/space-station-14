using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    // This partial part of the explosion system has all of the functions used to facilitate explosions moving across grids.
    public sealed partial class ExplosionSystem : EntitySystem
    {
        private Dictionary<GridId, HashSet<Vector2i>> _gridEdges = new();

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

        private void UpdateTile(IMapGrid mapGrid, Vector2i gridIndices)
        {
            throw new NotImplementedException();
        }
    }
}
