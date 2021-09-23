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
        public List<HashSet<Vector2i>>? Tiles;

        public List<float>? Strength;

        public GridId? Grid;

        public int Damage;

        public int TargetTotalStrength;

        public ExplosionOverlayEvent(List<HashSet<Vector2i>>? tiles, List<float>? strength, GridId? grid, int targetTotalStrength, int damage)
        {
            Tiles = tiles;
            Grid = grid;
            Strength = strength;
            TargetTotalStrength = targetTotalStrength;
            Damage = damage;
        }
    }
}
