using System;
using Content.Server.Explosion;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.Chemistry.ReactionEffects
{
    [DataDefinition]
    public class ExplosionReactionEffect : IReactionEffect
    {
        /// <summary>
        ///     The type of explosion. Determines damage types and tile break chance scaling.
        /// </summary>
        [DataField("explosionType", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<ExplosionPrototype>))]
        public string ExplosionType = default!;

        /// <summary>
        ///     The max intensity the explosion can have at a given tile. Places an upper limit of damage & tile break
        ///     chance.
        /// </summary>
        [DataField("maxIntensity")]
        public float MaxIntensity = 5;

        /// <summary>
        ///     How quickly intensity drops off as you move away from the epicenter
        /// </summary>
        [DataField("intensitySlope")]
        public float IntensitySlope = 1;

        /// <summary>
        ///     The maximum total intensity that this chemical reaction can achieve.
        /// </summary>
        /// <remarks>
        ///     A slope of 1 & MaxTotalIntensity of 100 corresponds to a radius of around 4.5 tiles.
        /// </remarks>
        [DataField("maxTotalIntensity")]
        public float MaxTotalIntensity = 100;

        /// <summary>
        ///     The intensity of the explosion per unit reaction.
        /// </summary>
        [DataField("intensityPerUnit")]
        public float IntensityPerUnit = 1;

        public void React(Solution solution, IEntity solutionEntity, double quantity)
        {
            var intensity = (float) Math.Min(quantity * IntensityPerUnit, MaxTotalIntensity);

            EntitySystem.Get<ExplosionSystem>().QueueExplosion(
                solutionEntity.Uid,
                ExplosionType,
                intensity,
                IntensitySlope,
                MaxIntensity);
        }
    }
}


