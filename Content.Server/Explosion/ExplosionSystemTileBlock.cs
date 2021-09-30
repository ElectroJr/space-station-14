using System;
using System.Collections.Generic;
using Content.Server.Atmos.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    public sealed partial class ExplosionSystem : EntitySystem
    {
        /// <summary>
        ///     This dictionary specifies the "strength" of the strongest anchored airtight entity on any given tile.
        /// </summary>
        public Dictionary<GridId, Dictionary<Vector2i, int>> BlockerMap = new();


        // I'm going to cut a corners here.
        // while it IS possible to have different entities blocking in different directions (i.e windoor to south, REINFOREED windoor to north
        // I'm just gonna treat every airtright component to have the same intensity, capped at the larger one.
        // so the reinfoced windoor will functionally make the un-reinforced windoor tougher


        // How to deal with diagonals in cardinal blocking?
        // well.... ANY time a diagonal tile is meant to be added.
        // It's OWN cardinal neighbours need to already have been added.
        // so imply check when adding a diagonal tile: is at least one of its neighbours already in the exploding set (and not blocked?)


        /// <summary>
        ///     The strength of an entity was updated. IF it is anchored, update the tolerance of the tile it is on.
        /// </summary>
        public void UpdateTolerance(EntityUid uid, ITransformComponent? transform = null)
        {
            if (!Resolve(uid, ref transform))
                return;

            UpdateTolerance(transform.GridID, transform.Coordinates);
        }

        /// <summary>
        ///     Get a list of all explosion blocking entities and use the largest explosion tolerance to determine the blocking strength.
        /// </summary>
        public void UpdateTolerance(IMapGrid grid, Vector2i tile)
        {
            int tolerance = 0;
            foreach (var uid in grid.GetAnchoredEntities(tile))
            {
                if (EntityManager.TryGetComponent(uid, out AirtightComponent? airtight))
                    tolerance = Math.Max(tolerance, airtight.ExplosionTolerance);
            }

            Dictionary<Vector2i, int>? tileTolerances;
            if (!BlockerMap.TryGetValue(grid.Index, out tileTolerances))
            {
                tileTolerances = new();
                BlockerMap.Add(grid.Index, tileTolerances);
            }

            if (tolerance > 0)
                tileTolerances[tile] = tolerance;
            else if (tileTolerances.ContainsKey(tile))
                tileTolerances.Remove(tile);
        }

        /// <summary>
        ///     Get a list of all explosion blocking entities and use the largest explosion tolerance to determine the blocking strength.
        /// </summary>
        public void UpdateTolerance(GridId gridId, Vector2i tile)
        {
            if (_mapManager.TryGetGrid(gridId, out var grid))
                 UpdateTolerance(grid, tile);
        }

        /// <summary>
        ///     Get a list of all explosion blocking entities and use the largest explosion tolerance to determine the blocking strength.
        /// </summary>
        public void UpdateTolerance(GridId gridId, EntityCoordinates pos)
        {
            if (_mapManager.TryGetGrid(gridId, out var grid))
                UpdateTolerance(grid, grid.CoordinatesToTile(pos));
        }
    }
}
