using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using System;
using System.Collections.Generic;

namespace Content.Shared.Explosion
{
    [Serializable, NetSerializable]
    public class ExplosionOverlayEvent : EntityEventArgs
    {
        public List<HashSet<Vector2i>>? ExplosionData;

        public GridId? GridData;

        public int Damage;

        public int TotalStrength;

        public ExplosionOverlayEvent(List<HashSet<Vector2i>>? explosionData, GridId? gridData, int totalStrength, int damage)
        {
            ExplosionData = explosionData;
            GridData = gridData;
            TotalStrength = totalStrength;
            Damage = damage;
        }
    }
}
