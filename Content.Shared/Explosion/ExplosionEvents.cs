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
        public MapCoordinates Epicenter;

        public List<HashSet<Vector2i>> Tiles;

        public List<float> Intensity;

        public GridId Grid;

        public ExplosionEvent(MapCoordinates epicenter, List<HashSet<Vector2i>> tiles, List<float> intensity, GridId grid)
        {
            Epicenter = epicenter;
            Tiles = tiles;
            Grid = grid;
            Intensity = intensity;
        }
    }

    /// <summary>
    ///     Update visual rendering of the explosion to correspond to the servers processing of it.
    /// </summary>
    [Serializable, NetSerializable]
    public class ExplosionUpdateEvent : EntityEventArgs
    {
        // EXPLOSION TODO map this onto a single explosion. It breaks if used on more than one explosion

        public int TileIndex;

        public ExplosionUpdateEvent(int tileIndex)
        {
            TileIndex = tileIndex;
        }
    }

    /// <summary>
    ///     Used  to preview Admin explosion spawning.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class ExplosionOverlayEvent : ExplosionEvent
    {
        public float Slope;
        public float TotalIntensity;

        public ExplosionOverlayEvent(MapCoordinates epicenter, List<HashSet<Vector2i>> tiles, List<float> intensity, GridId grid, float slope, float totalIntensity)
            : base(epicenter, tiles, intensity, grid)
        {
            Slope = slope;
            TotalIntensity = totalIntensity;
        }

        /// <summary>
        ///     Used to clear the currently shown overlay.
        /// </summary>
        public static ExplosionOverlayEvent Empty = new(MapCoordinates.Nullspace, new List<HashSet<Vector2i>>(), new List<float> (), GridId.Invalid, 0, 0);
    }
}
