using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Log;
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
                    component.Damage.DamageDict.TryAdd(type, 0);
                }

                foreach (var groupID in damageContainerPrototype.SupportedGroups)
                {
                    var group = _prototypeManager.Index<DamageGroupPrototype>(groupID);
                    foreach (var type in group.DamageTypes)
                    {
                        component.Damage.DamageDict.TryAdd(type, 0);
                    }
                }
            }
            else
            {
                // No DamageContainerPrototype was given. So we will allow the container to support all damage types
                foreach (var type in _prototypeManager.EnumeratePrototypes<DamageTypePrototype>())
                {
                    component.Damage.DamageDict.TryAdd(type.ID, 0);
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
        public void DamageChanged(DamageableComponent component, DamageSpecifier? damageDelta = null)
        {
            component.DamagePerGroup = component.Damage.GetDamagePerGroup();
            component.TotalDamage = component.Damage.Total;
            component.Dirty();
            RaiseLocalEvent(component.Owner.Uid, new DamageChangedEvent(component, damageDelta), false);
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
        public DamageSpecifier? TryChangeDamage(EntityUid uid, DamageSpecifier damage, bool ignoreResistances = false)
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
            if (!ignoreResistances && damageable.DamageModifierSetId != null)
            {
                if (_prototypeManager.TryIndex<DamageModifierSetPrototype>(damageable.DamageModifierSetId, out var modifierSet))
                {
                    damage = DamageSpecifier.ApplyModifierSet(damage, modifierSet);
                }

                if (damage.Empty)
                {
                    return damage;
                }
            }

            // Copy the current damage, for calculating the difference
            DamageSpecifier oldDamage = new(damageable.Damage);

            damageable.Damage.ExclusiveAdd(damage);
            damageable.Damage.ClampMin(0);

            var delta = damageable.Damage - oldDamage;
            delta.TrimZeros();

            if (!delta.Empty)
            {
                DamageChanged(damageable, delta);
            }

            return delta;
        }

        /// <summary>
        ///     Sets all damage types supported by a <see cref="DamageableComponent"/> to the specified value.
        /// </summary>
        /// <remakrs>
        ///     Does nothing If the given damage value is negative.
        /// </remakrs>
        public void SetAllDamage(DamageableComponent component, int newValue)
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
        ///     Figure out how much you need to scale some baseDamage such that the final total damage after resistances
        ///     are applied is at least equal to the requested amount. Basically "I have an object with X resistances.
        ///     How much damage to I need to deal to it to ACTUALLY damage it by at-least a given amount"?
        /// </summary>
        /// <returns></returns>
        public float InverseResistanceSolve(DamageableComponent component, DamageSpecifier baseDamage, int damageTarget, float maxScale = 100, float precision = 1)
        {
            if (damageTarget == 0)
                return 0;

            // First, we take out base input damage and remove any damage types that are not actually applicable to the target
            DamageSpecifier damage = new(baseDamage);
            foreach (var type in damage.DamageDict.Keys)
            {
                if (!component.Damage.DamageDict.ContainsKey(type))
                    damage.DamageDict.Remove(type);
            }

            // Is there even any applicable damage?
            damage.TrimZeros();
            if (damage.Empty)
                return float.NaN;

            // Resolve this component's damage modifier
            DamageModifierSetPrototype? modifier = null;
            if (component.DamageModifierSetId != null)
            {
                IoCManager.Resolve<IPrototypeManager>().TryIndex(component.DamageModifierSetId, out modifier);
            }

            // using the modifier, define a function that maps a damage scaling factor to the distance from the desired damage
            Func<float, int> damageDelta;
            int sign = Math.Sign(damageTarget);
            if (modifier == null)
                damageDelta = scale => sign * ((scale * damage).Total - damageTarget);
            else
                damageDelta = scale => sign * (DamageSpecifier.ApplyModifierSet(scale * damage, modifier).Total - damageTarget);

            // Note that resultingDamage is not a monotonic function. Consider a resistance set that has:
            // - maps burn damage 1:1
            // - reduces blunt damage by 20 (to a min of zero), and then multiplies by -5 (healing)
            // initially, as damage increases the burn damage goes up, and total damage goes up.
            // but when input blunt damage becomes larger than 20, the blunt damage will be begin healing, eventually overpowering the burn damage.

            // the scale factor only goes as low as 0. We use this as one endpoint of our search
            // the damageDelta at x0 is always negative
            float x0 = 0;

            // for the other search endpoint (x1) we use the maximum scale.
            // here the damage delta SHOULD always be positive
            float x1 = maxScale;
            var y1 = damageDelta(x1);

            if (damageDelta(x1) < 0)
            {
                // Well apparently it isn't positive for this x1 value.
                // MAYBE the maxScale is not big enough. OR MAYBE there is a root, but as mentioned the function is not monotonic, so we can't be sure.
                // so lets try another endpoint.

                // what scale is needed if the target has NO resistance set?
                x1 = (float) damageTarget / damage.Total;

                if (damageDelta(x1) < 0)
                {
                    // welp at least we tried
                    return float.NaN;
                }
            }

            // begin a bisection search.
            // If the output wasn't an integer, id use some sort of gradient based search. there probably is some way of doing it with ints, but eh fuck it.
            float xGuess;
            int result;
            do
            {
                xGuess = (x0 + x1) / 2;
                result = damageDelta(x1);

                if (result == 0)
                    break;

                if (result < 0)
                    x0 = xGuess;
                else
                    x1 = xGuess;

            } while (Math.Abs(x1 - x0) > precision);

            return xGuess;
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

        public DamageChangedEvent(DamageableComponent damageable, DamageSpecifier? damageDelta)
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
        }
    }
}
