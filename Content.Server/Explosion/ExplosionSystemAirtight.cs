using System;
using System.Collections.Generic;
using Content.Server.Atmos.Components;
using Content.Server.Destructible;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{
    public sealed partial class ExplosionSystem : EntitySystem
    {

        [Dependency] private readonly DestructibleSystem _destructibleSystem = default!;

        // The explosion intensity required to break an entity depends on the explosion type. So it is stored in a
        // Dictionary<string, float>
        //
        // Hence, each tile has a (Dictionary<string, float>, AtmosDirection) value. This specifies what directions are
        // blocked, and how intense a given explosion type needs to be in order to destroy ALL airtight entities on that
        // tile. This is the TileData struct.
        //
        // We then need this data for every tile on a grid. So this mess of a variable maps the Grid ID and Vector2i
        // grid
        // indices to this tile-data.
        //
        // Indexing a Dictionary with Vector2ikeys is faster than using a TileRef or (GridId, Vector2i) tuple key. So
        // given that we process one grid at a time, AFAIK a nested dictionary is just the best option here performance
        // wise?
        private Dictionary<GridId, Dictionary<Vector2i, TileData>> _airtightMap = new();
        // EXPLOSION TODO fix shit code

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
        ///     what directions are blocked, along with the largest explosion tolerance. Note that as we only keep track
        ///     of the largest tolerance, this means that the explosion map will actually be inaccurate if you have
        ///     something like a normal and a reinforced windoor on the same tile. But given that this is a pretty rare
        ///     occurrence, I am fine with this.
        /// </remarks>
        public void UpdateAirtightMap(IMapGrid grid, Vector2i tile)
        {
            Dictionary<string, float>  tolerance = new();
            var blockedDirections = AtmosDirection.Invalid;

            if (!_airtightMap.ContainsKey(grid.Index))
                _airtightMap[grid.Index] = new();

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
                _airtightMap[grid.Index][tile] = new(tolerance, blockedDirections);
            else
                _airtightMap[grid.Index].Remove(tile);
        }

        /// <summary>
        ///     On receiving damage, re-evaluate how much explosion damage is needed to destroy an airtight entity.
        /// </summary>
        private void OnAirtightDamaged(EntityUid uid, AirtightComponent airtight, DamageChangedEvent args)
        {
            airtight.ExplosionTolerance = GetExplosionTolerance(uid);

            // do we need to update our explosion blocking map?
            if (!airtight.AirBlocked)
                return;
            if (!EntityManager.TryGetComponent(uid, out TransformComponent transform) || !transform.Anchored)
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
            // How much total damage is needed to destroy this entity? This also includes "break" behaviors. This
            // ASSUMES that this will result in a non-airtight entity.
            var totalDamageTarget = _destructibleSystem.DestroyedAt(uid);

            Dictionary<string, float> explosionTolerance = new();

            if (totalDamageTarget == FixedPoint2.MaxValue)
                return explosionTolerance;

            // What multiple of each explosion type damage set will result in the damage exceeding the required amount?
            foreach (var type in _prototypeManager.EnumeratePrototypes<ExplosionPrototype>())
            {   
                explosionTolerance[type.ID] =
                    _damageableSystem.InverseResistanceSolve(uid, type.DamagePerIntensity, totalDamageTarget, 1);
            }

            return explosionTolerance;
        }
    }

    /// <summary>
    ///     Internal data struct that describes the explosion-blocking airtight entities on a tile.
    /// </summary>
    internal struct TileData
    {
        public TileData(Dictionary<string, float> explosionTolerance, AtmosDirection blockedDirections)
        {
            ExplosionTolerance = explosionTolerance;
            BlockedDirections = blockedDirections;
        }

        public Dictionary<string, float> ExplosionTolerance;
        public AtmosDirection BlockedDirections = AtmosDirection.Invalid;
    }
}
