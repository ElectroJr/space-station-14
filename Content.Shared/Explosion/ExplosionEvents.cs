using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared.Explosion
{
    /// <summary>
    ///     An explosion event. Used for client side rendering.
    /// </summary>
    [Serializable, NetSerializable]
    public class ExplosionEvent : EntityEventArgs
    {
        public List<HashSet<Vector2i>> Tiles;

        public List<float> Intensity;

        public GridId Grid;

        public ExplosionEvent(List<HashSet<Vector2i>> tiles, List<float> intensity, GridId grid)
        {
            Tiles = tiles;
            Grid = grid;
            Intensity = intensity;
        }
    }

    /// <summary>
    ///     Used for Admin explosion spawning.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class ExplosionOverlayEvent : ExplosionEvent
    {
        public int Damage;
        public int TotalIntensity;

        public ExplosionOverlayEvent(List<HashSet<Vector2i>> tiles, List<float> intensity, GridId grid, int damage, int totalIntensity)
            : base(tiles, intensity, grid)
        {
            Damage = damage;
            TotalIntensity = totalIntensity;
        }

        /// <summary>
        ///     Used to clear the currently shown overlay.
        /// </summary>
        public static ExplosionOverlayEvent Empty = new(new List<HashSet<Vector2i>>(), new List<float> (), GridId.Invalid, 0, 0);
    }
}
