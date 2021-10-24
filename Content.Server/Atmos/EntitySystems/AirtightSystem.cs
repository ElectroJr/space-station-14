using Content.Server.Atmos.Components;
using Content.Server.Destructible;
using Content.Server.Explosion;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using System;
using System.Collections.Generic;

namespace Content.Server.Atmos.EntitySystems
{
    [UsedImplicitly]
    public class AirtightSystem : EntitySystem
    {
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly AtmosphereSystem _atmosphereSystem = default!;
        [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
        [Dependency] private readonly DamageableSystem _damageableSystem = default!;
        [Dependency] private readonly DestructibleSystem _destructibleSystem = default!;

        public const int EntitiesUpdatedPerTick = 10;

        /// <summary>
        ///     A queue to evaluate the entity explosion tolerance.
        /// </summary>
        /// <remarks>
        ///     Without a queue, this requires a massive amount of calculations in individual ticks whenever explosions
        ///     happen.
        /// </remarks>
        private readonly Queue<EntityUid> _outdatedEntities = new();

        public override void Initialize()
        {
            SubscribeLocalEvent<AirtightComponent, ComponentInit>(OnAirtightInit);
            SubscribeLocalEvent<AirtightComponent, ComponentShutdown>(OnAirtightShutdown);
            SubscribeLocalEvent<AirtightComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<AirtightComponent, AnchorStateChangedEvent>(OnAirtightPositionChanged);
            SubscribeLocalEvent<AirtightComponent, RotateEvent>(OnAirtightRotated);
            SubscribeLocalEvent<AirtightComponent, DamageChangedEvent>(OnDamageChanged);
        }

        public bool Updating;

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var toUpdate = EntitiesUpdatedPerTick;
            while (toUpdate > 0 && _outdatedEntities.TryDequeue(out var entity))
            {
                UpdateExplosionTolerance(entity);
                toUpdate--;
                Updating = true;
            }

            if (Updating && _outdatedEntities.Count == 0)
            {
                Updating = false;
                Logger.Info("Finished computing airtight integrity.");
            }
        }

        private void OnAirtightInit(EntityUid uid, AirtightComponent airtight, ComponentInit args)
        {
            if (airtight.FixAirBlockedDirectionInitialize)
            {
                var rotateEvent = new RotateEvent(airtight.Owner, Angle.Zero, airtight.Owner.Transform.WorldRotation);
                OnAirtightRotated(uid, airtight, ref rotateEvent);
            }

            // Adding this component will immediately anchor the entity, because the atmos system
            // requires airtight entities to be anchored for performance.
            airtight.Owner.Transform.Anchored = true;

            if (airtight.ExplosionTolerance == null)
            {
                // No tolerance was specified, we have to compute our own.
                _outdatedEntities.Enqueue(uid);
                return;
            }

            UpdatePosition(airtight);
        }

        private void OnAirtightShutdown(EntityUid uid, AirtightComponent airtight, ComponentShutdown args)
        {
            SetAirblocked(airtight, false);

            if (airtight.FixVacuum)
            {
                _atmosphereSystem.FixVacuum(airtight.LastPosition.Item1, airtight.LastPosition.Item2);
            }
        }

        private void OnDamageChanged(EntityUid uid, AirtightComponent component, DamageChangedEvent args) => _outdatedEntities.Enqueue(uid);

        private void OnMapInit(EntityUid uid, AirtightComponent airtight, MapInitEvent args)
        {
        }

        private void OnAirtightPositionChanged(EntityUid uid, AirtightComponent airtight, ref AnchorStateChangedEvent args)
        {
            var gridId = airtight.Owner.Transform.GridID;
            var coords = airtight.Owner.Transform.Coordinates;

            var grid = _mapManager.GetGrid(gridId);
            var tilePos = grid.TileIndicesFor(coords);

            // Update and invalidate new position.
            airtight.LastPosition = (gridId, tilePos);
            InvalidatePosition(gridId, tilePos);
        }

        private void OnAirtightRotated(EntityUid uid, AirtightComponent airtight, ref RotateEvent ev)
        {
            if (!airtight.RotateAirBlocked || airtight.InitialAirBlockedDirection == (int) AtmosDirection.Invalid)
                return;

            airtight.CurrentAirBlockedDirection = (int) Rotate((AtmosDirection) airtight.InitialAirBlockedDirection, ev.NewRotation);
            UpdatePosition(airtight);
        }

        public void SetAirblocked(AirtightComponent airtight, bool airblocked)
        {
            airtight.AirBlocked = airblocked;
            UpdatePosition(airtight);
        }

        public void UpdatePosition(AirtightComponent airtight)
        {
            if (!airtight.Owner.Transform.Anchored || !airtight.Owner.Transform.GridID.IsValid())
                return;

            var grid = _mapManager.GetGrid(airtight.Owner.Transform.GridID);
            airtight.LastPosition = (airtight.Owner.Transform.GridID, grid.TileIndicesFor(airtight.Owner.Transform.Coordinates));
            InvalidatePosition(airtight.LastPosition.Item1, airtight.LastPosition.Item2);
        }

        public void InvalidatePosition(GridId gridId, Vector2i pos)
        {
            if (!gridId.IsValid())
                return;

            _explosionSystem.UpdateTolerance(gridId, pos);
            _atmosphereSystem.UpdateAdjacent(gridId, pos);
            _atmosphereSystem.InvalidateTile(gridId, pos);
        }

        private AtmosDirection Rotate(AtmosDirection myDirection, Angle myAngle)
        {
            var newAirBlockedDirs = AtmosDirection.Invalid;

            if (myAngle == Angle.Zero)
                return myDirection;

            // TODO ATMOS MULTIZ: When we make multiZ atmos, special case this.
            for (var i = 0; i < Atmospherics.Directions; i++)
            {
                var direction = (AtmosDirection) (1 << i);
                if (!myDirection.IsFlagSet(direction)) continue;
                var angle = direction.ToAngle();
                angle += myAngle;
                newAirBlockedDirs |= angle.ToAtmosDirectionCardinal();
            }

            return newAirBlockedDirs;
        }

        /// <summary>
        ///     How much explosion damage is needed to destroy an air-blocking entity?
        /// </summary>
        private void UpdateExplosionTolerance(EntityUid uid, AirtightComponent? airtight = null)
        {
            if (!Resolve(uid, ref airtight, logMissing: false))
                return;

            airtight.ExplosionTolerance = new();
            foreach (var type in _prototypeManager.EnumeratePrototypes<ExplosionPrototype>())
            {
                // how much total damage is needed to destroy this entity?
                var damageNeeded = _destructibleSystem.DestroyedAt(uid);

                if (!float.IsNaN(damageNeeded))
                {
                    // What multiple of the explosion set will achieve this?
                    var maxScale = 10 * damageNeeded / type.DamagePerIntensity.Total;
                    airtight.ExplosionTolerance[type.ID] = _damageableSystem.InverseResistanceSolve(uid, type.DamagePerIntensity, (int) Math.Ceiling(damageNeeded), maxScale);
                }

                _explosionSystem.UpdateTolerance(uid);
            }
        }
    }
}
