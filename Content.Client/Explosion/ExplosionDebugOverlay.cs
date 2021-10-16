using JetBrains.Annotations;
using Robust.Client.Graphics;
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
    public sealed class ExplosionDebugOverlay : Overlay
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;

        public List<HashSet<Vector2i>> Tiles = new();
        public List<float> Intensity = new();
        public IMapGrid? Grid;
        public float TotalIntensity;
        public float Slope;

        public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

        private readonly Font _font;

        public ExplosionDebugOverlay()
        {
            IoCManager.InjectDependencies(this);

            var cache = IoCManager.Resolve<IResourceCache>();
            _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 8);
        }

        protected override void Draw(in OverlayDrawArgs args)
        {

            if (Grid == null)
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
            var gridXform = _entityManager.GetComponent<ITransformComponent>(Grid!.GridEntityId);

            for (int i = 2; i < Tiles.Count; i++)
            {
                foreach (var tile in Tiles[i])
                {
                    var worldCenter = gridXform.WorldMatrix.Transform((Vector2) tile + 0.5f);
                    var screenCenter = _eyeManager.WorldToScreen(worldCenter);
                    
                    if (Intensity![i] > 9)
                        screenCenter += (-12, -8);
                    else
                        screenCenter += (-8, -8);

                    handle.DrawString(_font, screenCenter, Intensity![i].ToString("F2"));
                }
            }

            foreach (var epicenter in Tiles[1])
            {
                var worldCenter = gridXform.WorldMatrix.Transform((Vector2) epicenter + 0.5f);
                var screenCenter = _eyeManager.WorldToScreen(worldCenter) + (-24, -24);
                string text = $"{Intensity![1]:F2}\nΣ={TotalIntensity:F1}\nΔ={Slope:F1}";

                handle.DrawString(_font, screenCenter, text);
            }
        }

        private void DrawWorld(in OverlayDrawArgs args)
        {
            var handle = args.WorldHandle;
            var gridXform = _entityManager.GetComponent<ITransformComponent>(Grid!.GridEntityId);

            for (int i = 0; i < Tiles.Count; i++)
            {
                var color = ColorMap(Intensity![i]);
                var colorTransparent = color;
                colorTransparent.A = 0.4f;

                foreach (var tile in Tiles[i])
                {
                    var centre = gridXform.WorldMatrix.Transform((Vector2) tile + 0.5f);
                    var box = Box2.UnitCentered.Translated(centre);
                    var rotatedBox = new Box2Rotated(box, gridXform.WorldRotation, centre);                    

                    handle.DrawRect(rotatedBox, color, false);
                    handle.DrawRect(rotatedBox, colorTransparent);
                }
            }
        }

        private Color ColorMap(float intensity)
        {
            var frac = 1 - intensity / Intensity![1];
            Color result;
            if (frac < 0.5f)
                result = Color.InterpolateBetween(Color.Red, Color.Orange, frac * 2);
            else
                result = Color.InterpolateBetween(Color.Orange, Color.Yellow, (frac - 0.5f) * 2);
            return result;
        }
    }
}
