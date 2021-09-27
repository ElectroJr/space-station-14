using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    [UsedImplicitly]
    public sealed class ExplosionOverlay : Overlay
    {
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        public List<HashSet<Vector2i>>? Tiles;
        public List<float>? Strength;
        public IMapGrid? Grid;
        public int TargetTotalStrength;
        public int Damage;

        public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

        // Fire overlays, stolen from atmos
        private const int FireStates = 3;
        private const string FireRsiPath = "/Textures/Effects/fire.rsi";

        private readonly float[] _fireTimer = new float[FireStates];
        private readonly float[][] _fireFrameDelays = new float[FireStates][];
        private readonly int[] _fireFrameCounter = new int[FireStates];
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
                _fireFrameDelays[i] = state.GetDelays();
                _fireFrameCounter[i] = 0;
            }
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            var drawHandle = args.WorldHandle;
            var mapId = _eyeManager.CurrentMap;
            var worldBounds = _eyeManager.GetWorldViewbounds();

           /* foreach (var mapGrid in _mapManager.FindGridsIntersecting(mapId, worldBounds))
            {
                if (!_gasTileOverlaySystem.HasData(mapGrid.Index))
                    continue;

                drawHandle.SetTransform(mapGrid.WorldMatrix);

                foreach (var tile in mapGrid.GetTilesIntersecting(worldBounds))
                {
                    foreach (var (texture, color) in _gasTileOverlaySystem.GetOverlays(mapGrid.Index, tile.GridIndices))
                    {
                        drawHandle.DrawTexture(texture, new Vector2(tile.X, tile.Y), color);
                    }
                }
            }*/

            drawHandle.SetTransform(Matrix3.Identity);

        }
    }
}
