using System;
using Content.Server.Chemistry.Components.SolutionManager;
using Content.Server.Explosion;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.Explosion;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.Chemistry.ReactionEffects
{
    [DataDefinition]
    public class ExplosionReactionEffect : ReagentEffect
    {
        /// <summary>
        ///     The type of explosion. Determines damage types and tile break chance scaling.
        /// </summary>
        [DataField("explosionType", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<ExplosionPrototype>))]
        public string ExplosionType = default!;

        /// <summary>
        ///     The max intensity the explosion can have at a given tile. Places an upper limit of damage and tile break
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
        ///     The maximum total intensity that this chemical reaction can achieve. Basically here to prevent people
        ///     from creating a nuke by collecting enough potassium and water.
        /// </summary>
        /// <remarks>
        ///     A slope of 1 and MaxTotalIntensity of 100 corresponds to a radius of around 4.5 tiles.
        /// </remarks>
        [DataField("maxTotalIntensity")]
        public float MaxTotalIntensity = 100;

        /// <summary>
        ///     The intensity of the explosion per unit reaction.
        /// </summary>
        [DataField("intensityPerUnit")]
        public float IntensityPerUnit = 1;

        public override bool ShouldLog => true;
        public override LogImpact LogImpact => LogImpact.High;

        public override void Effect(ReagentEffectArgs args)
        {
            var intensity = (float) Math.Min(args.Quantity * IntensityPerUnit, MaxTotalIntensity);

            EntitySystem.Get<ExplosionSystem>().QueueExplosion(
                uid,
                ExplosionType,
                intensity,
                IntensitySlope,
                MaxIntensity);
        }
    }
}
