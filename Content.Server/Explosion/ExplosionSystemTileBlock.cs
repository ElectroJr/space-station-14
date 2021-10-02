using System;
using System.Collections.Generic;
using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    public sealed partial class ExplosionSystem : EntitySystem
    {
        public Dictionary<GridId, Dictionary<Vector2i, (int, AtmosDirection)>> BlockerMap = new();

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
        ///     Update the map of explosion blockers.
        /// </summary>
        /// <remarks>
        /// Gets a list of all airtight entities on a tile. Assembles a <see cref="AtmosDirection"/> that specifies what
        /// directions are blocked, along with the largest explosion tolerance. Note that this means that the explosion
        /// map will actually be inaccurate if you have something like a windoor & a reinforced windoor on the same
        /// tile.
        /// </remarks>
        public void UpdateTolerance(IMapGrid grid, Vector2i tile)
        {
            int tolerance = 0;
            var blockedDirections = AtmosDirection.Invalid;

            foreach (var uid in grid.GetAnchoredEntities(tile))
            {
                if (EntityManager.TryGetComponent(uid, out AirtightComponent? airtight) && airtight.AirBlocked)
                {
                    tolerance = Math.Max(tolerance, airtight.ExplosionTolerance);
                    blockedDirections |= airtight.AirBlockedDirection;
                }
            }

            Dictionary<Vector2i, (int, AtmosDirection)>? tileTolerances;
            if (!BlockerMap.TryGetValue(grid.Index, out tileTolerances))
            {
                tileTolerances = new();
                BlockerMap.Add(grid.Index, tileTolerances);
            }

            if (tolerance > 0 && blockedDirections != AtmosDirection.Invalid)
                tileTolerances[tile] = (tolerance, blockedDirections);
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
