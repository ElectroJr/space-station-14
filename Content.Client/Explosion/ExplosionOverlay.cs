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
        ///     The size of the explosion annulus that is drawn at any given time
        /// </summary>
        public const int Size = 4;

        /// <summary>
        ///     The set of explosions to draw on the overlay.
        /// </summary>
        internal List<ExplosionEvent> Explosions = new();

        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IRobustRandom _robustRandom = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

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
            var mapId = _eyeManager.CurrentMap;
            var worldBounds = _eyeManager.GetWorldViewbounds();

            foreach (var explosion in Explosions)
            {
                var grid = _mapManager.GetGrid(explosion.Grid);
                drawHandle.SetTransform(grid.WorldMatrix);

                for (var i = 1; i <= Size; i--)
                {
                    if (i > explosion.Tiles.Count)
                        break;

                    DrawExplodingTiles(drawHandle, grid, explosion.Tiles[^i], explosion.Intensity[^i], worldBounds);
                }
            }

            drawHandle.SetTransform(Matrix3.Identity);
        }

        private int IntensityToState(float intensity)
        {
            return (int) Math.Min((intensity / 5), FireStates) - 1;
        }

        private void DrawExplodingTiles(DrawingHandleWorld drawHandle, IMapGrid grid, HashSet<Vector2i> tiles, float intensity, Box2Rotated worldBounds)
        {
            var state = IntensityToState(intensity);

            foreach (var tile in tiles)
            {
                if (!worldBounds.Contains(grid.GridTileToWorldPos(tile)))
                    return;

                var texture = _robustRandom.Pick(_fireFrames[state]);
                drawHandle.DrawTexture(texture, new Vector2(tile.X, tile.Y));
            }
        }
    }
}
