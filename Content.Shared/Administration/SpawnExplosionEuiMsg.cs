using Content.Shared.Eui;
using Robust.Shared.Serialization;
using System;
using Robust.Shared.Map;
using System.Collections.Generic;
using Robust.Shared.Maths;
using Content.Shared.Explosion;

namespace Content.Shared.Administration
{
    public static class SpawnExplosionEuiMsg
    {
        [Serializable, NetSerializable]
        public sealed class Close : EuiMessageBase { }

        /// <summary>
        ///     This message is sent to the server to request explosion preview data.
        /// </summary>
        [Serializable, NetSerializable]
        public class PreviewRequest : EuiMessageBase
        {
            public readonly MapCoordinates Epicenter;
            public readonly string TypeId;
            public readonly HashSet<Vector2i> Excluded;
            public readonly float TotalIntensity;
            public readonly float IntensitySlope;
            public readonly float MaxIntensity;

            public PreviewRequest(MapCoordinates epicenter, string typeId, HashSet<Vector2i> excluded, float totalIntensity, float intensitySlope, float maxIntensity)
            {
                Epicenter = epicenter;
                TypeId = typeId;
                Excluded = excluded;
                TotalIntensity = totalIntensity;
                IntensitySlope = intensitySlope;
                MaxIntensity = maxIntensity;
            }
        }

        /// <summary>
        ///     This message is used to send explosion-preview data to the client.
        /// </summary>
        [Serializable, NetSerializable]
        public class PreviewData : EuiMessageBase
        {
            public readonly float Slope;
            public readonly float TotalIntensity;
            public readonly ExplosionEvent Explosion;

            public PreviewData(MapCoordinates epicenter, string typeID, List<HashSet<Vector2i>> tiles, List<float> intensity, GridId grid, float slope, float totalIntensity)
            {
                Slope = slope;
                TotalIntensity = totalIntensity;
                Explosion = new(epicenter, typeID, tiles, intensity, grid);
            }
        }
    }
}
