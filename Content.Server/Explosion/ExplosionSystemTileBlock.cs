using System;
using System.Collections.Generic;
using Content.Server.Atmos.Components;
using Content.Server.Destructible;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    public sealed partial class ExplosionSystem : EntitySystem
    {

        [Dependency] private readonly DestructibleSystem _destructibleSystem = default!;

        // Each tile has a (Dictionary<string, float>, AtmosDirection) value. This specifies what directions are
        // blocked, and how much damage an explosion needs to deal in order to destroy the blocking entity. This mess of
        // a variable maps the Grid ID and Vector2i grid indices to these values.
        public Dictionary<GridId, Dictionary<Vector2i, (Dictionary<string, float>, AtmosDirection)>> AirtightMap = new();

        public void UpdateAirtightMap(GridId gridId, Vector2i tile)
        {
            if (_mapManager.TryGetGrid(gridId, out var grid))
                UpdateAirtightMap(grid, tile);
        }

        /// <summary>
        ///     Update the map of explosion blockers.
        /// </summary>
        /// <remarks>
        ///     Gets a list of all airtight entities on a tile. Assembles a <see cref="AtmosDirection"/> that specifies
        ///     what directions are blocked, along with the largest explosion tolerance. Note that this means that the
        ///     explosion map will actually be inaccurate if you have something like a windoor & a reinforced windoor on
        ///     the same tile.
        /// </remarks>
        public void UpdateAirtightMap(IMapGrid grid, Vector2i tile)
        {
            Dictionary<string, float>  tolerance = new();
            var blockedDirections = AtmosDirection.Invalid;

            if (!AirtightMap.ContainsKey(grid.Index))
                AirtightMap[grid.Index] = new();

            foreach (var uid in grid.GetAnchoredEntities(tile))
            {
                if (!EntityManager.TryGetComponent(uid, out AirtightComponent? airtight) || !airtight.AirBlocked)
                    continue;

                blockedDirections |= airtight.AirBlockedDirection;
                airtight.ExplosionTolerance ??= GetExplosionTolerance(uid);
                foreach (var (type, value) in airtight.ExplosionTolerance)
                {
                    if (!tolerance.TryAdd(type, value))
                        tolerance[type] = Math.Max(tolerance[type], value);
                }
            }

            if (blockedDirections != AtmosDirection.Invalid)
                AirtightMap[grid.Index][tile] = (tolerance, blockedDirections);
            else
                AirtightMap[grid.Index].Remove(tile);
        }

        /// <summary>
        ///     How much explosion damage is needed to destroy an air-blocking entity?
        /// </summary>
        private void OnAirtightDamaged(EntityUid uid, AirtightComponent airtight, DamageChangedEvent args)
        {
            airtight.ExplosionTolerance = GetExplosionTolerance(uid);

            // do we need to update our explosion blocking map?
            if (!airtight.AirBlocked)
                return;

            if (!EntityManager.TryGetComponent(uid, out ITransformComponent transform) || !transform.Anchored)
                return;

            if (!_mapManager.TryGetGrid(transform.GridID, out var grid))
                return;

            UpdateAirtightMap(grid, grid.CoordinatesToTile(transform.Coordinates));
        }

        /// <summary>
        ///     Return a dictionary that specifies how intense a given explosion type needs to be in order to destroy an entity.
        /// </summary>
        public Dictionary<string, float> GetExplosionTolerance(EntityUid uid)
        {
            // how much total damage is needed to destroy this entity?
            var totalDamageTarget = MathF.Ceiling(_destructibleSystem.DestroyedAt(uid));

            Dictionary<string, float> explosionTolerance = new();

            if (float.IsNaN(totalDamageTarget))
                return explosionTolerance;

            // What multiple of each explosion type damage set will result in the damage exceeding the required amount?
            foreach (var type in _prototypeManager.EnumeratePrototypes<ExplosionPrototype>())
            {   
                explosionTolerance[type.ID] =
                    _damageableSystem.InverseResistanceSolve(uid, type.DamagePerIntensity, totalDamageTarget);
            }

            return explosionTolerance;
        }
    }
}
