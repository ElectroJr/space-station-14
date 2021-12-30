using System.Collections.Generic;
using System.Linq;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Shared.Damage
{
    public class DamageableSystem : EntitySystem
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<DamageableComponent, ComponentInit>(DamageableInit);
            SubscribeLocalEvent<DamageableComponent, ComponentHandleState>(DamageableHandleState);
            SubscribeLocalEvent<DamageableComponent, ComponentGetState>(DamageableGetState);
        }

        /// <summary>
        ///     Initialize a damageable component
        /// </summary>
        private void DamageableInit(EntityUid uid, DamageableComponent component, ComponentInit _)
        {
            if (component.DamageContainerID != null &&
                _prototypeManager.TryIndex<DamageContainerPrototype>(component.DamageContainerID,
                out var damageContainerPrototype))
            {
                // Initialize damage dictionary, using the types and groups from the damage
                // container prototype
                foreach (var type in damageContainerPrototype.SupportedTypes)
                {
                    component.Damage.DamageDict.TryAdd(type, FixedPoint2.Zero);
                }

                foreach (var groupID in damageContainerPrototype.SupportedGroups)
                {
                    var group = _prototypeManager.Index<DamageGroupPrototype>(groupID);
                    foreach (var type in group.DamageTypes)
                    {
                        component.Damage.DamageDict.TryAdd(type, FixedPoint2.Zero);
                    }
                }
            }
            else
            {
                // No DamageContainerPrototype was given. So we will allow the container to support all damage types
                foreach (var type in _prototypeManager.EnumeratePrototypes<DamageTypePrototype>())
                {
                    component.Damage.DamageDict.TryAdd(type.ID, FixedPoint2.Zero);
                }
            }

            component.DamagePerGroup = component.Damage.GetDamagePerGroup();
            component.TotalDamage = component.Damage.Total;
        }

        /// <summary>
        ///     Directly sets the damage specifier of a damageable component.
        /// </summary>
        /// <remarks>
        ///     Useful for some unfriendly folk. Also ensures that cached values are updated and that a damage changed
        ///     event is raised.
        /// </remarks>
        public void SetDamage(DamageableComponent damageable, DamageSpecifier damage)
        {
            damageable.Damage = damage;
            DamageChanged(damageable);
        }

        /// <summary>
        ///     If the damage in a DamageableComponent was changed, this function should be called.
        /// </summary>
        /// <remarks>
        ///     This updates cached damage information, flags the component as dirty, and raises a damage changed event.
        ///     The damage changed event is used by other systems, such as damage thresholds.
        /// </remarks>
        public void DamageChanged(DamageableComponent component, DamageSpecifier? damageDelta = null,
            bool interruptsDoAfters = true)
        {
            component.DamagePerGroup = component.Damage.GetDamagePerGroup();
            component.TotalDamage = component.Damage.Total;
            component.Dirty();

            if (EntityManager.TryGetComponent<AppearanceComponent>(component.Owner, out var appearance) && damageDelta != null)
                appearance.SetData(DamageVisualizerKeys.DamageUpdateGroups, damageDelta.GetDamagePerGroup().Keys.ToList());
            RaiseLocalEvent(component.Owner, new DamageChangedEvent(component, damageDelta, interruptsDoAfters), false);
        }

        /// <summary>
        ///     Applies damage specified via a <see cref="DamageSpecifier"/>.
        /// </summary>
        /// <remarks>
        ///     <see cref="DamageSpecifier"/> is effectively just a dictionary of damage types and damage values. This
        ///     function just applies the container's resistances (unless otherwise specified) and then changes the
        ///     stored damage data. Division of group damage into types is managed by <see cref="DamageSpecifier"/>.
        /// </remarks>
        /// <returns>
        ///     Returns a <see cref="DamageSpecifier"/> with information about the actual damage changes. This will be
        ///     null if the user had no applicable components that can take damage.
        /// </returns>
        public DamageSpecifier? TryChangeDamage(EntityUid? uid, DamageSpecifier damage, bool ignoreResistances = false,
            bool interruptsDoAfters = true)
        {
            if (!EntityManager.TryGetComponent<DamageableComponent>(uid, out var damageable))
            {
                // TODO BODY SYSTEM pass damage onto body system
                return null;
            }

            if (damage == null)
            {
                Logger.Error("Null DamageSpecifier. Probably because a required yaml field was not given.");
                return null;
            }

            if (damage.Empty)
            {
                return damage;
            }

            // Apply resistances
            if (!ignoreResistances)
            {
                if (damageable.DamageModifierSetId != null &&
                    _prototypeManager.TryIndex<DamageModifierSetPrototype>(damageable.DamageModifierSetId, out var modifierSet))
                {
                    damage = DamageSpecifier.ApplyModifierSet(damage, modifierSet);
                }

                var ev = new DamageModifyEvent(damage);
                RaiseLocalEvent(uid.Value, ev, false);
                damage = ev.Damage;

                if (damage.Empty)
                {
                    return damage;
                }
            }

            // Copy the current damage, for calculating the difference
            DamageSpecifier oldDamage = new(damageable.Damage);

            damageable.Damage.ExclusiveAdd(damage);
            damageable.Damage.ClampMin(FixedPoint2.Zero);

            var delta = damageable.Damage - oldDamage;
            delta.TrimZeros();

            if (!delta.Empty)
            {
                DamageChanged(damageable, delta, interruptsDoAfters);
            }

            return delta;
        }

        /// <summary>
        ///     Sets all damage types supported by a <see cref="DamageableComponent"/> to the specified value.
        /// </summary>
        /// <remakrs>
        ///     Does nothing If the given damage value is negative.
        /// </remakrs>
        public void SetAllDamage(DamageableComponent component, FixedPoint2 newValue)
        {
            if (newValue < 0)
            {
                // invalid value
                return;
            }

            foreach (var type in component.Damage.DamageDict.Keys)
            {
                component.Damage.DamageDict[type] = newValue;
            }

            // Setting damage does not count as 'dealing' damage, even if it is set to a larger value, so we pass an
            // empty damage delta.
            DamageChanged(component, new DamageSpecifier());
        }

        private void DamageableGetState(EntityUid uid, DamageableComponent component, ref ComponentGetState args)
        {
            args.State = new DamageableComponentState(component.Damage.DamageDict, component.DamageModifierSetId);
        }

        private void DamageableHandleState(EntityUid uid, DamageableComponent component, ref ComponentHandleState args)
        {
            if (args.Current is not DamageableComponentState state)
            {
                return;
            }

            component.DamageModifierSetId = state.ModifierSetId;

            // Has the damage actually changed?
            DamageSpecifier newDamage = new() { DamageDict = state.DamageDict };
            var delta = component.Damage - newDamage;
            delta.TrimZeros();

            if (!delta.Empty)
            {
                component.Damage = newDamage;
                DamageChanged(component, delta);
            }
        }

        /// <summary>
        ///     This function figures out by how much you need to scale some baseDamage such that the final total damage
        ///     of a given damageable component (after resistances are applied) is equal to the requested amount.
        /// </summary>
        /// <remarks>
        ///     It turns out applying a damage modifier / resistance set is easy, but the reverse is somewhat
        ///     convoluted. In the worst case scenario, this ends up finding a root using bisection. This solver also
        ///     assumes that this process does not involve any healing. I.e., the base damage specifier has no healing
        ///     and the <see cref="DamageModifierSet"/> has only positive coefficients. This ensures that the final
        ///     damage is a monotonic function.
        /// </remarks>
        /// <returns>Returns a multiplier if it can find it, otherwise returns float.NaN</returns>
        public float InverseResistanceSolve(EntityUid uid, DamageSpecifier baseDamage,
            FixedPoint2 totalDamageTarget, FixedPoint2 tolerance, DamageableComponent? damageable = null)
        {
            if (!Resolve(uid, ref damageable))
                return float.NaN;

            if (damageable.TotalDamage > totalDamageTarget)
                return 0;

            totalDamageTarget -= damageable.TotalDamage;
            Dictionary<string, FixedPoint2> damage = new();

            // Include only damage types that are actually applicable to the container.
            foreach (var (type, quantity) in baseDamage.DamageDict)
            {
                // Does the container support this type?
                if (!damageable.Damage.DamageDict.ContainsKey(type))
                    continue;

                // Currently, this doesn't support a mix of healing and damage
                if (quantity < 0)
                    return float.NaN;

                damage.Add(type, quantity);
            }

            // Resolve this component's damage modifier
            DamageModifierSetPrototype? modifier;
            if (damageable.DamageModifierSetId == null ||
                !_prototypeManager.TryIndex(damageable.DamageModifierSetId, out modifier))
            {
                // No modifier. This makes the calculation trivial.
                return (float) (totalDamageTarget / damage.Values.Sum());
            }

            // adjust each damage type by the modifier set coefficients
            Dictionary<string, FixedPoint2> reductions = new();
            foreach (var type in damage.Keys)
            {
                if (!modifier.Coefficients.TryGetValue(type, out var coeff))
                    coeff = 1;

                // We don't support a mix of healing and damage
                if (coeff < 0)
                    return float.NaN;

                damage[type] *= coeff;

                if (modifier.FlatReduction.TryGetValue(type, out var red))
                    reductions[type] = red * coeff;
            }
            var totalDamage = damage.Values.Sum();

            if (reductions.Count == 0)
            {
                // No applicable flat reductions. Again, this makes the calculation trivial.
                return (float) (totalDamageTarget / totalDamage);
            }

            // We **might** need to perform a bisection search. here we define a function that maps a damage scaling factor to the
            // distance from the desired final damage

            FixedPoint2 CalcDamage(float scale)
            {
                FixedPoint2 result = FixedPoint2.Zero;
                foreach (var (type, quantity) in damage)
                {
                    result += FixedPoint2.Max(FixedPoint2.Zero, scale * quantity - reductions.GetValueOrDefault(type));
                }

                return result;
            }

            // Next we define the endpoins of the bisection search. First: what is the maximum scale that could possibly
            // be required? Given that the flat reductions cannot reduce the incoming damage below zero, the actual
            // reduction may be less than total reduction. But the total reduction gives the upper limit on how much the
            // damage could be reduced. So assuming full reduction leads to the largest possible scale guess.
            float x1 = (float) ((totalDamageTarget + reductions.Values.Sum()) / totalDamage);

            // Next, we check that the maximum value is not just the correct result. This actually happens most of the
            // time when it comes to explosions (which is currently the only thing that uses this calculation).
            // Fortunately this means we usually do not actually need to do a bisection search!
            if (CalcDamage(x1).EqualsApprox(totalDamageTarget, tolerance))
                return x1;

            // fuck.

            // For the minimum value, we just use 0.
            float x0 = 0;

            // for the initial guess, we just ignore the flat reductions
            float xGuess = (float) (totalDamageTarget / totalDamage);
            var result = CalcDamage(xGuess);

            // avoid stuck loop, just in case something goes wrong
            var maxIteration = 100;
            var iteration = 0; 

            // begin a bisection search.
            while (!result.EqualsApprox(totalDamageTarget, tolerance) && iteration < maxIteration)
            {
                iteration++;

                if (result < totalDamageTarget)
                    x0 = xGuess;
                else
                    x1 = xGuess;

                xGuess = (x0 + x1) / 2;
                result = CalcDamage(xGuess);
            }

            if (iteration == maxIteration)
                Logger.Error($"DamageableSystem.InverseResistanceSolve got stuck in infinite loop when processing entity {uid}");

            return xGuess;
        }
    }

    /// <summary>
    ///     Raised on an entity when damage is about to be dealt,
    ///     in case anything else needs to modify it other than the base
    ///     damageable component.
    ///
    ///     For example, armor.
    /// </summary>
    public class DamageModifyEvent : EntityEventArgs
    {
        public DamageSpecifier Damage;

        public DamageModifyEvent(DamageSpecifier damage)
        {
            Damage = damage;
        }
    }

    public class DamageChangedEvent : EntityEventArgs
    {
        /// <summary>
        ///     This is the component whose damage was changed.
        /// </summary>
        /// <remarks>
        ///     Given that nearly every component that cares about a change in the damage, needs to know the
        ///     current damage values, directly passing this information prevents a lot of duplicate
        ///     Owner.TryGetComponent() calls.
        /// </remarks>
        public readonly DamageableComponent Damageable;

        /// <summary>
        ///     The amount by which the damage has changed. If the damage was set directly to some number, this will be
        ///     null.
        /// </summary>
        public readonly DamageSpecifier? DamageDelta;

        /// <summary>
        ///     Was any of the damage change dealing damage, or was it all healing?
        /// </summary>
        public readonly bool DamageIncreased = false;

        /// <summary>
        ///     Does this event interrupt DoAfters?
        ///     Note: As provided in the constructor, this *does not* account for DamageIncreased.
        ///     As written into the event, this *does* account for DamageIncreased.
        /// </summary>
        public readonly bool InterruptsDoAfters = false;

        public DamageChangedEvent(DamageableComponent damageable, DamageSpecifier? damageDelta, bool interruptsDoAfters)
        {
            Damageable = damageable;
            DamageDelta = damageDelta;

            if (DamageDelta == null)
                return;

            foreach (var damageChange in DamageDelta.DamageDict.Values)
            {
                if (damageChange > 0)
                {
                    DamageIncreased = true;
                    break;
                }
            }
            InterruptsDoAfters = interruptsDoAfters && DamageIncreased;
        }
    }
}
