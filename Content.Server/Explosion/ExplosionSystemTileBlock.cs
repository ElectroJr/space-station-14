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
        // TODO EXPLOSION uuhh... do this better cause this is some garbage
        public Dictionary<GridId, Dictionary<Vector2i, (Dictionary<string, float>, AtmosDirection)>> AirtightMap = new();

        /// <summary>
        ///     Update the map of explosion blockers.
        /// </summary>
        /// <remarks>
        ///     Gets a list of all airtight entities on a tile. Assembles a <see cref="AtmosDirection"/> that specifies
        ///     what directions are blocked, along with the largest explosion tolerance. Note that this means that the
        ///     explosion map will actually be inaccurate if you have something like a windoor & a reinforced windoor on
        ///     the same tile.
        /// </remarks>
        public void UpdateTolerance(IMapGrid grid, Vector2i tile)
        {
            Dictionary<string, float>  tolerance = new();
            var blockedDirections = AtmosDirection.Invalid;

            if (!AirtightMap.ContainsKey(grid.Index))
                AirtightMap[grid.Index] = new();

            foreach (var uid in grid.GetAnchoredEntities(tile))
            {
                if (EntityManager.TryGetComponent(uid, out AirtightComponent? airtight) &&
                    airtight.AirBlocked &&
                    airtight.ExplosionTolerance != null)
                {
                    foreach (var (type, value) in airtight.ExplosionTolerance)
                    {
                        if (!tolerance.TryAdd(type, value))
                            tolerance[type] = Math.Max(tolerance[type], value);
                    }

                    blockedDirections |= airtight.AirBlockedDirection;
                }
            }

            if (tolerance.Count > 0 && blockedDirections != AtmosDirection.Invalid)
                AirtightMap[grid.Index][tile] = (tolerance, blockedDirections);
            else
                AirtightMap[grid.Index].Remove(tile);
        }


        /// <summary>
        ///     The strength of an entity was updated. IF it is anchored, update the tolerance of the tile it is on.
        /// </summary>
        public void UpdateTolerance(EntityUid uid, ITransformComponent? transform = null)
        {
            if (!Resolve(uid, ref transform))
                return;

            if (_mapManager.TryGetGrid(transform.GridID, out var grid))
                UpdateTolerance(grid, grid.CoordinatesToTile(transform.Coordinates));
        }

        /// <summary>
        ///     Get a list of all explosion blocking entities and use the largest explosion tolerance to determine the blocking strength.
        /// </summary>
        public void UpdateTolerance(GridId gridId, Vector2i tile)
        {
            if (_mapManager.TryGetGrid(gridId, out var grid))
                 UpdateTolerance(grid, tile);
        }
    }
}
