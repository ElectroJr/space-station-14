using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using System;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    [UsedImplicitly]
    public sealed class ExplosionOverlay : Overlay
    {
        /// <summary>
        /// Fire.rsi has 3 fire states, and two other states that we dont want.
        /// </summary>
        private const int TotalFireStates = 3;

        /// <summary>
        ///     The explosion that needs to be drawn. This explosion is currently being processed by the server and
        ///     expanding outwards.
        /// </summary>
        internal Explosion? ActiveExplosion;

        /// <summary>
        ///     This index specifies what parts of the currently expanding explosion should be drawn.
        /// </summary>
        public int Index;

        /// <summary>
        ///     These explosions have finished expanding, but we will draw for a few more frames. This is important for
        ///     small explosions, as otherwise they disappear far too quickly.
        /// </summary>
        internal List<Explosion> CompletedExplosions = new ();

        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IRobustRandom _robustRandom = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

        /// <summary>
        ///     How intense does the explosion have to be at a tile to advance to the next fire texture state?
        /// </summary>
        public const int IntensityPerState = 12;

        public ExplosionOverlay()
        {
            IoCManager.InjectDependencies(this);
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            var drawHandle = args.WorldHandle;
            var worldBounds = _eyeManager.GetWorldViewbounds();

            if (ActiveExplosion != null)
                DrawExplosion(drawHandle, worldBounds, ActiveExplosion, Index);

            foreach (var exp in CompletedExplosions)
            {
                DrawExplosion(drawHandle, worldBounds, exp, exp.Tiles.Count);
            }

            drawHandle.SetTransform(Matrix3.Identity);
        }

        private void DrawExplosion(DrawingHandleWorld drawHandle, Box2Rotated worldBounds, Explosion exp, int index)
        {
            drawHandle.SetTransform(exp.Grid.WorldMatrix);
            var gridBounds = exp.Grid.InvWorldMatrix.TransformBox(worldBounds);

            for (var j = 0; j < index; j++)
            {
                var frames = exp.Frames[(int) Math.Min(exp.Intensity[j] / IntensityPerState, TotalFireStates - 1)];
                DrawExplodingTiles(drawHandle, exp.Grid, exp.Tiles[j], gridBounds, frames);
            }
        }

        private void DrawExplodingTiles(DrawingHandleWorld drawHandle, IMapGrid grid, HashSet<Vector2i> tiles, Box2 bounds, Texture[] frames)
        {
            foreach (var tile in tiles)
            {
                if (!bounds.Contains(grid.GridTileToLocal(tile).Position))
                    continue;

                var texture = _robustRandom.Pick(frames);
                drawHandle.DrawTexture(texture, new Vector2(tile.X, tile.Y));
            }
        }
    }
}
