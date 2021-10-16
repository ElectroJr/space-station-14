using System.Collections.Generic;
using Content.Shared.Damage;
using Content.Shared.Sound;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared.Explosion
{
    [Prototype("explosion")]
    public class ExplosionPrototype : IPrototype
    {
        [DataField("id", required: true)]
        public string ID { get; } = default!;

        /// <summary>
        ///     Damage to deal to entities. This is scaled by the explosion intensity.
        /// </summary>
        [DataField("damagePerIntensity", required: true)]
        public DamageSpecifier DamagePerIntensity = default!;

        /// <summary>
        ///     This dictionary maps the explosion intensity to a tile break chance. For values in between, linear
        ///     interpolation is used.
        /// </summary>
        [DataField("tileBreakChance")]
        public Dictionary<float, float> TileBreakChance = new() { {0f, 0f }, {15f, 1f} };

        /// <summary>
        ///     When a tile is broken by an explosion, the intensity is reduced by this amount and is used to try and
        ///     break the tile a second time. This is repeated until a roll fails or the tile has become space.
        /// </summary>
        /// <remarks>
        ///     If this number is too small, even relatively weak explosions can have a non-zero
        ///     chance to create a space tile.
        /// </remarks>
        [DataField("breakRerollReduction")]
        public float BreakRerollReduction = 10f;

        /// <summary>
        ///     Color emited by a point light at the centre of the explosion.
        /// </summary>
        [DataField("lightColor")]
        public Color LightColor = Color.Orange;

        /// <summary>
        ///     Color used to modulate the atmos-plasma-fire effect.
        /// </summary>
        [DataField("fireModColor")]
        public Color? FireModColor;

        [DataField("Sound")]
        public SoundSpecifier Sound = new SoundCollectionSpecifier("explosion");

        [DataField("texturePath")]
        public string TexturePath = "/Textures/Effects/fire.rsi";
    }
}
