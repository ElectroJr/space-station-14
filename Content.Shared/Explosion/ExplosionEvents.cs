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

        public GridId GridId;

        public string TypeID;

        public ExplosionEvent(MapCoordinates epicenter, string typeID, List<HashSet<Vector2i>> tiles, List<float> intensity, GridId gridId)
        {
            Epicenter = epicenter;
            Tiles = tiles;
            GridId = gridId;
            Intensity = intensity;
            TypeID = typeID;
        }
    }

    /// <summary>
    ///     Update visual rendering of the explosion to correspond to the servers processing of it.
    /// </summary>
    [Serializable, NetSerializable]
    public class ExplosionOverlayUpdateEvent : EntityEventArgs
    {
        public int Index;

        public ExplosionOverlayUpdateEvent(int index)
        {
            Index = index;
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

        public ExplosionOverlayEvent(MapCoordinates epicenter, string typeID, List<HashSet<Vector2i>> tiles, List<float> intensity, GridId grid, float slope, float totalIntensity)
            : base(epicenter, typeID, tiles, intensity, grid)
        {
            Slope = slope;
            TotalIntensity = totalIntensity;
        }

        /// <summary>
        ///     Used to clear the currently shown overlay.
        /// </summary>
        public static ExplosionOverlayEvent Empty = new(MapCoordinates.Nullspace, "", new List<HashSet<Vector2i>>(), new List<float> (), GridId.Invalid, 0, 0);
    }
}
