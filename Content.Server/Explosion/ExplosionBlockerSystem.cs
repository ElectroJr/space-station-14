using System;
using System.Collections.Generic;
using Content.Server.Construction.Components;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Server.Explosion.Components;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Server.Explosion
{


    public sealed class ExplosionBlockerSystem : EntitySystem
    {

        // TODO
        // currently do not support different maps.
        // shoudl really be using TileRefs

        public const int EntitiesUpdatedPerTick = 10;

        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;

        /// <summary>
        ///     A queue to evaluate the entity explosion tolerance.
        /// </summary>
        /// <remarks>
        ///     Without a queue, this requires a massive amount of calculations in individual ticks whenever explosions
        ///     happen.
        /// </remarks>
        private readonly Queue<EntityUid> _outdatedEntities = new();

        /// <summary>
        ///     This dictionary specifies the "strength" of the strongest anchored explosion blocker on any given tile.
        /// </summary>
        public Dictionary<GridId, Dictionary<Vector2i, int>> BlockerMap = new();

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<ExplosionBlockerComponent, ComponentInit>(InitializeExplosionBlocker);
            SubscribeLocalEvent<ExplosionBlockerComponent, ComponentShutdown>(ShutdownExplosionBlocker);
            SubscribeLocalEvent<ExplosionBlockerComponent, DamageChangedEvent>(HandleDamageChanged);
            SubscribeLocalEvent<ExplosionBlockerComponent, AnchoredEvent>(HandleEntityAnchored);
            SubscribeLocalEvent<ExplosionBlockerComponent, UnanchoredEvent>(HandleEntityUnanchored);
        }

        private void ShutdownExplosionBlocker(EntityUid uid, ExplosionBlockerComponent component, ComponentShutdown args)
        {
            component.Tolerance = 0;
            UpdateTileStrength(uid, component);
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var toUpdate = EntitiesUpdatedPerTick;
            while (toUpdate > 0 && _outdatedEntities.TryDequeue(out var entity))
            {
                UpdateTolerance(entity);
                toUpdate--;
            }
        }

        private void HandleEntityUnanchored(EntityUid uid, ExplosionBlockerComponent component, UnanchoredEvent args)
        {
            // TODO QUESTION these should really be event arguments, no?
            // get the grid & tile
            var entity = EntityManager.GetEntity(uid);
            var grid = _mapManager.GetGrid(entity.Transform.GridID);

            if (!BlockerMap.TryGetValue(grid.Index, out var tileTolerances))
                return;

            var tile = grid.CoordinatesToTile(entity.Transform.Coordinates);

            if (!tileTolerances.TryGetValue(tile, out var tolerance))
                return;

            if (tolerance > component.Tolerance)
                return;

            // This component was (or was tied with) the most resilient anchored entity on this tile.
            // --> we need to completely update this tile.
            UpdateTileStrength(grid, tile);
        }

        private void HandleEntityAnchored(EntityUid uid, ExplosionBlockerComponent component, AnchoredEvent args)
        {
            if (component.Tolerance == 0)
                return;

            if (!ComponentManager.TryGetComponent(uid, out ITransformComponent? transform))
                return;

            // get the grid & tile
            var grid = _mapManager.GetGrid(transform.GridID);
            var tile = grid.CoordinatesToTile(transform.Coordinates);

            if (!BlockerMap.ContainsKey(grid.Index))
            {
                BlockerMap.Add(grid.Index, new() { { tile, component.Tolerance } });
                return;
            }

            Dictionary<Vector2i, int>? tileTolerances;
            if (!BlockerMap.TryGetValue(grid.Index, out tileTolerances))
            {
                tileTolerances = new();
                BlockerMap.Add(grid.Index, tileTolerances);
            }

            tileTolerances[tile] = Math.Max(tileTolerances[tile], component.Tolerance);
        }

        private void InitializeExplosionBlocker(EntityUid uid, ExplosionBlockerComponent component, ComponentInit args)
        {
            if (component.Tolerance == 0)
            {
                // No tolerance was specified, we have to compute our own.
                _outdatedEntities.Enqueue(uid);
                return;
            }

            UpdateTileStrength(uid, component);
        }

        /// <summary>
        ///     The strength of an entity was updated. IF it is anchored, update the tolerance of the tile it is on.
        /// </summary>
        private void UpdateTileStrength(EntityUid uid, ExplosionBlockerComponent? component = null, ITransformComponent? transform = null)
        {
            if (!Resolve(uid, ref component, ref transform))
                return;

            if (!transform.Anchored)
                return;

            var grid = _mapManager.GetGrid(transform.GridID);
            var tile = grid.CoordinatesToTile(transform.Coordinates);
            UpdateTileStrength(grid, tile);
        }

        /// <summary>
        ///     Get a list of all explosion blocking entities and use the largest explosion tolerance to determine the blocking strength.
        /// </summary>
        private void UpdateTileStrength(IMapGrid grid, Vector2i tile)
        {
            int strength = 0;
            foreach (var uid in grid.GetAnchoredEntities(tile))
            {
                if (ComponentManager.TryGetComponent(uid, out ExplosionBlockerComponent? blocker))
                    strength = Math.Max(strength, blocker.Tolerance);
            }

            Dictionary<Vector2i, int>? tileTolerances;
            if (!BlockerMap.TryGetValue(grid.Index, out tileTolerances))
            {
                tileTolerances = new();
                BlockerMap.Add(grid.Index, tileTolerances);
            }

            if (strength > 0)
                tileTolerances[tile] = strength;
            else if (tileTolerances.ContainsKey(tile))
                tileTolerances.Remove(tile);
        }

        private void HandleDamageChanged(EntityUid uid, ExplosionBlockerComponent component, DamageChangedEvent args)
        {
            _outdatedEntities.Enqueue(uid);
        }

        /// <summary>
        ///     How much damage is needed to destroy a blocking entity?
        /// </summary>
        /// <remarks>
        ///     Wow this is stupidly fucking hard to calculate.
        ///     The damage needed to destroy an entity should REALLY be a property of the damageable component.
        /// </remarks>
        private void UpdateTolerance(
            EntityUid uid,
            ExplosionBlockerComponent? blocker = null,
            DamageableComponent? damageable = null,
            DestructibleComponent? destructible = null)
        {
            if (!Resolve(uid, ref blocker, ref damageable, ref destructible))
                return;

            blocker.Tolerance = int.MaxValue;

            // Note we have nested for loops, but the vast majority of components only have one threshold with 1-3 behaviors.
            // Really, this should JUST be a property of the damageable component.
            var damageNeeded = int.MaxValue;
            foreach (var threshold in destructible.Thresholds)
            {
                // the below ONLY works if the threshold trigger is a total-damage type trigger
                if (threshold.Trigger is not DamageTrigger trigger)
                    continue;

                foreach (var behavior in threshold.Behaviors)
                {
                    if (behavior is DoActsBehavior actBehavior &&
                        actBehavior.HasAct(ThresholdActs.Destruction | ThresholdActs.Breakage))
                    {
                        // We have found our destructible threshold. what damage does it need?
                        damageNeeded = Math.Min(damageNeeded, trigger.Damage);
                    }
                }
            }

            damageNeeded -= damageable.TotalDamage;

            // This max scale assumes structures will generally not have resistances below ~ 0.2
            var maxScale = 10 * damageNeeded / _explosionSystem.BaseExplosionDamage.Total;

            var scale = _damageableSystem.InverseResistanceSolve(damageable, _explosionSystem.BaseExplosionDamage, damageNeeded, maxScale);

            if (scale != float.NaN)
                blocker.Tolerance = (int) Math.Ceiling(scale);

            UpdateTileStrength(uid, blocker);
        }


/*        /// <summary>
        ///     Look for entities on a tile that block explosions and return the largest explosion tolerance (the
        ///     blocking bottle neck).
        /// </summary>
        private int GetTileTolerance(IMapGrid grid, Vector2i tile)
        {
            var entities = grid.GetAnchoredEntities(tile);

            var explosionTolerance = 0;
            foreach (var uid in entities)
            {
                if (ComponentManager.TryGetComponent(uid, out ExplosionBlocker? blocker))
                    explosionTolerance = Math.Max(explosionTolerance, blocker.Tolerance);
            }

            return explosionTolerance;
        }


*/
    }
}
