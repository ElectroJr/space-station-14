using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    [UsedImplicitly]
    public sealed class ExplosionOverlay : Overlay
    {
        [Dependency] private readonly IComponentManager _componentManager = default!;
        //[Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;

        public List<HashSet<Vector2i>>? ReversedExplosionData;
        public IMapGrid? Grid;
        public int TotalStrength;
        public int Damage;

        public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;


        private readonly Font _font;
        private readonly Font _smallFont;

        public ExplosionOverlay()
        {
            IoCManager.InjectDependencies(this);


            var cache = IoCManager.Resolve<IResourceCache>();
            _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 16);
            _smallFont = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
        }

        public Vector2 GetCenter(IMapGrid grid, Vector2i tile)
        {
            var gridXform = _componentManager.GetComponent<ITransformComponent>(grid.GridEntityId);

            return gridXform.WorldMatrix.Transform((Vector2) tile + 0.5f);
        }

        public Box2 GetBounds(IMapGrid grid, Vector2i tile) => Box2.UnitCentered.Translated(GetCenter(grid, tile));
        //return new Box2Rotated(Box2.UnitCentered.Translated(center), -gridXform.WorldRotation, center);


        /// <inheritdoc />
        protected override void Draw(in OverlayDrawArgs args)
        {

            if (ReversedExplosionData == null || Grid == null)
                return;

            if (ReversedExplosionData.Count < 2 || ReversedExplosionData[ReversedExplosionData.Count-2].Count != 1)
                return;

            switch (args.Space)
            {
                case OverlaySpace.ScreenSpace:
                    DrawScreen(args);
                    break;
                case OverlaySpace.WorldSpace:
                    DrawWorld(args);
                    break;
            }

        }

        private void DrawScreen(OverlayDrawArgs args)
        {
            var handle = args.ScreenHandle;

            int str = 1;
            foreach (var tileSet in ReversedExplosionData!)
            {
                foreach (var tile in tileSet)
                {
                    DrawTile(handle, Grid!, tile, str);
                }
                str++;
            }

            foreach (var epicenter in ReversedExplosionData[str-3])
            {
                DrawEpicenterData(handle, Grid!, epicenter);
            }
        }

        private void DrawWorld(in OverlayDrawArgs args)
        {
            var handle = args.WorldHandle;
            int str = 1;
            foreach (var tileSet in ReversedExplosionData!)
            {
                foreach (var tile in tileSet)
                {
                    DrawTile(handle, Grid!, tile, str);
                }
                str++;
            }
        }

        private void DrawTile(DrawingHandleWorld handle, IMapGrid grid, Vector2i tile, int strength)
        {
            var bb = GetBounds(grid, tile);
            var color = ColorMap(strength);

            handle.DrawRect(bb, color, false);
            color.A = 0.4f;
            handle.DrawRect(bb, color);
        }

        private void DrawTile(DrawingHandleScreen handle, IMapGrid grid, Vector2i tile, int strength)
        {
            var coords = _eyeManager.WorldToScreen(GetCenter(grid, tile));

            if (strength > 9)
                coords += (-16, -16);
            else
                coords += (-8, -16);

            handle.DrawString(_font, coords, strength.ToString());
        }

        private void DrawEpicenterData(DrawingHandleScreen handle, IMapGrid grid, Vector2i tile)
        {
            var bb = GetBounds(grid, tile);

            var topLeft = _eyeManager.WorldToScreen(bb.TopLeft);
            var topRight = _eyeManager.WorldToScreen(bb.TopRight);

            if (Damage < 10)
                topRight -= (12, 0);
            else
                topRight -= (24, 0);

            handle.DrawString(_smallFont, topLeft, TotalStrength.ToString(), Color.Black);
            handle.DrawString(_smallFont, topRight, Damage.ToString(), Color.Black);
        }


        private Color ColorMap(int strength, bool transparent = false)
        {
            var interp = 1 - (float) strength / 10;
            Color result;
            if (interp < 0.5f)
            {
                result = Color.InterpolateBetween(Color.Red, Color.Orange, interp * 2);
            }
            else
            {
                result = Color.InterpolateBetween(Color.Orange, Color.Yellow, (interp - 0.5f) * 2);
            }
            return result;
        }
    }
}
