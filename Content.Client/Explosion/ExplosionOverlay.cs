using Content.Shared.Explosion;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
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
        ///     The set of explosions to draw on the overlay.
        /// </summary>
        internal List<ExplosionEvent> Explosions = new();

        /// <summary>
        ///     The indices that determine what parts of an explosion should currently be drawn.
        /// </summary>
        internal List<int> ExplosionIndices = new();

        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IRobustRandom _robustRandom = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

        /// <summary>
        ///     How intense does the explosion have to be at a tile to advance to the next fire texture state?
        /// </summary>
        public const int IntensityPerState = 10;

        // Fire overlays, stolen from atmos.
        private const int FireStates = 3;
        private const string FireRsiPath = "/Textures/Effects/fire.rsi";
        private readonly Texture[][] _fireFrames = new Texture[FireStates][];

        public ExplosionOverlay()
        {
            IoCManager.InjectDependencies(this);

            var fire = _resourceCache.GetResource<RSIResource>(FireRsiPath).RSI;

            for (var i = 0; i < FireStates; i++)
            {
                if (!fire.TryGetState((i + 1).ToString(), out var state))
                    throw new ArgumentOutOfRangeException($"Fire RSI doesn't have state \"{i}\"!");

                _fireFrames[i] = state.GetFrames(RSI.State.Direction.South);
            }
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            var drawHandle = args.WorldHandle;
            var worldBounds = _eyeManager.GetWorldViewbounds();

            for (var i = 0; i < Explosions.Count; i++)
            {
                var explosion = Explosions[i];
                var grid = _mapManager.GetGrid(explosion.Grid);
                var gridBounds = grid.InvWorldMatrix.TransformBox(worldBounds);
                drawHandle.SetTransform(grid.WorldMatrix);

                var maxJ = Math.Min(explosion.Tiles.Count, ExplosionIndices[i]);
                for (var j = 0; j < maxJ; j++)
                {
                    DrawExplodingTiles(drawHandle, grid, explosion.Tiles[j], explosion.Intensity[j], gridBounds);
                }
            }
            drawHandle.SetTransform(Matrix3.Identity);
        }

        private void DrawExplodingTiles(DrawingHandleWorld drawHandle, IMapGrid grid, HashSet<Vector2i> tiles, float intensity, Box2 bounds)
        {
            var state = (int) Math.Min((intensity / IntensityPerState), FireStates - 1);

            foreach (var tile in tiles)
            {
                if (!bounds.Contains(grid.GridTileToLocal(tile).Position))
                    continue;

                var texture = _robustRandom.Pick(_fireFrames[state]);
                drawHandle.DrawTexture(texture, new Vector2(tile.X, tile.Y));
            }
        }
    }
}
