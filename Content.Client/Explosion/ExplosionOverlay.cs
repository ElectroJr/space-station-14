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
        [Dependency] private readonly IEyeManager _eyeManager = default!;

        public List<HashSet<Vector2i>>? Tiles;
        public List<float>? Strength;
        public IMapGrid? Grid;
        public int TargetTotalStrength;
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

            if (Tiles == null || Grid == null)
                return;

            if (Tiles.Count < 2 || Tiles[1].Count != 1)
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

            float actualTotalStrength = 0;

            for (int i = 0; i < Tiles!.Count; i++)
            {
                foreach (var tile in Tiles[i])
                {
                    DrawTile(handle, Grid!, tile, Strength![i]);
                }
                actualTotalStrength += Strength![i] * Tiles[i].Count;
            }

            foreach (var epicenter in Tiles[1])
            {
                DrawEpicenterData(handle, Grid!, epicenter, actualTotalStrength);
            }
        }

        private void DrawWorld(in OverlayDrawArgs args)
        {
            var handle = args.WorldHandle;

            for (int i = 0; i < Tiles!.Count; i++)
            {
                foreach (var tile in Tiles[i])
                {
                    DrawTile(handle, Grid!, tile, Strength![i]);
                }
            }
        }

        private void DrawTile(DrawingHandleWorld handle, IMapGrid grid, Vector2i tile, float strength)
        {
            var bb = GetBounds(grid, tile);
            var color = ColorMap(strength);

            handle.DrawRect(bb, color, false);
            color.A = 0.4f;
            handle.DrawRect(bb, color);
        }

        private void DrawTile(DrawingHandleScreen handle, IMapGrid grid, Vector2i tile, float strength)
        {
            var coords = _eyeManager.WorldToScreen(GetCenter(grid, tile));

            if (strength > 9)
                coords += (-26, -16);
            else
                coords += (-18, -16);

            handle.DrawString(_font, coords, strength.ToString("F1"));
        }

        private void DrawEpicenterData(DrawingHandleScreen handle, IMapGrid grid, Vector2i tile, float actualTotalStrength)
        {
            var bb = GetBounds(grid, tile);

            var topLeft = _eyeManager.WorldToScreen(bb.TopLeft);
            var topRight = _eyeManager.WorldToScreen(bb.TopRight);
            var bottomLeft = _eyeManager.WorldToScreen(bb.BottomLeft);

            if (actualTotalStrength < 10)
                topRight -= (28, 0);
            else
                topRight -= (30, 0);

            bottomLeft += (0, -24);

            handle.DrawString(_smallFont, topLeft, TargetTotalStrength.ToString(), Color.Black);
            handle.DrawString(_smallFont, topRight, actualTotalStrength.ToString("F1"), Color.Black);
            handle.DrawString(_smallFont, bottomLeft, Damage.ToString(), Color.Black);
        }


        private Color ColorMap(float strength)
        {
            var interp = 1- strength / Strength![1];
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
