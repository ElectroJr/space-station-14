using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using System.Collections.Generic;
using System.Linq;

namespace Content.Client.Administration.UI.SpawnExplosion
{
    [UsedImplicitly]
    public sealed class ExplosionDebugOverlay : Overlay
    {
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;

        public Dictionary<GridId, Dictionary<int, HashSet<Vector2i>>> Tiles = new();
        public List<float> Intensity = new();
        public float TotalIntensity;
        public float Slope;

        public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;

        public Matrix3 SpaceMatrix;
        public MapId Map;

        private readonly Font _font;

        public ExplosionDebugOverlay()
        {
            IoCManager.InjectDependencies(this);

            var cache = IoCManager.Resolve<IResourceCache>();
            _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 8);
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if (Map != _eyeManager.CurrentMap)
                return;

            if (Tiles.Count == 0)
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
            handle.SetTransform(Matrix3.Identity);

            foreach (var (gridId, tileSetList) in Tiles)
            {

                Matrix3 matrix;
                if (gridId.IsValid())
                {
                    if (!_mapManager.TryGetGrid(gridId, out var grid))
                        continue;

                    var gridXform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
                    matrix = gridXform.WorldMatrix;
                }
                else
                {
                    matrix = SpaceMatrix;
                }

                for (int i = 0; i < Intensity.Count; i++)
                {
                    if (!tileSetList.TryGetValue(i, out var tiles)) continue;
                    foreach (var tile in tiles)
                    {
                        var worldCenter = matrix.Transform((Vector2) tile + 0.5f);

                        // is the center of this tile visible to the user?
                        if (!args.WorldBounds.Contains(worldCenter))
                            continue;

                        var screenCenter = _eyeManager.WorldToScreen(worldCenter);

                        if (Intensity![i] > 9)
                            screenCenter += (-12, -8);
                        else
                            screenCenter += (-8, -8);

                        handle.DrawString(_font, screenCenter, Intensity![i].ToString("F2"));
                    }
                }

                if (tileSetList.ContainsKey(0))
                {
                    var epicenter = tileSetList[0].First();
                    var worldCenter = matrix.Transform((Vector2) epicenter + 0.5f);
                    var screenCenter = _eyeManager.WorldToScreen(worldCenter) + (-24, -24);
                    string text = $"{Intensity![0]:F2}\nΣ={TotalIntensity:F1}\nΔ={Slope:F1}";
                    handle.DrawString(_font, screenCenter, text);
                }
            }
        }

        private void DrawWorld(in OverlayDrawArgs args)
        {
            var handle = args.WorldHandle;
            handle.SetTransform(Matrix3.Identity);
            
            foreach (var (gridId, tileSetList) in Tiles)
            {
                Matrix3 matrix;
                Box2 gridBounds;
                if (gridId.IsValid())
                {
                    if (!_mapManager.TryGetGrid(gridId, out var grid))
                        continue;

                    var gridXform = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
                    matrix = gridXform.WorldMatrix;
                    gridBounds = gridXform.InvWorldMatrix.TransformBox(args.WorldBounds);
                }
                else
                {
                    matrix = SpaceMatrix;
                    gridBounds = Matrix3.Invert(matrix).TransformBox(args.WorldBounds);
                }

                handle.SetTransform(matrix);

                for (int i = 0; i < Intensity.Count; i++)
                {
                    var color = ColorMap(Intensity![i]);
                    var colorTransparent = color;
                    colorTransparent.A = 0.4f;


                    if (!tileSetList.TryGetValue(i, out var tiles)) continue;
                    foreach (var tile in tiles)
                    {
                        // is the center of this tile visible to the user?
                        if (!gridBounds.Contains((Vector2) tile + 0.5f))
                            continue;

                        var box = Box2.UnitCentered.Translated((Vector2) tile + 0.5f);
                        handle.DrawRect(box, color, false);
                        handle.DrawRect(box, colorTransparent);
                    }
                }
            }
        }

        private Color ColorMap(float intensity)
        {
            var frac = 1 - intensity / Intensity![0];
            Color result;
            if (frac < 0.5f)
                result = Color.InterpolateBetween(Color.Red, Color.Orange, frac * 2);
            else
                result = Color.InterpolateBetween(Color.Orange, Color.Yellow, (frac - 0.5f) * 2);
            return result;
        }
    }
}
