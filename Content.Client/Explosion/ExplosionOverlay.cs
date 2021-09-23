using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Client.Explosion
{
    [UsedImplicitly]
    public sealed class ExplosionOverlay : Overlay
    {
        [Dependency] private readonly IComponentManager _componentManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;


        private readonly Font _font;

        public ExplosionOverlay()
        {
            IoCManager.InjectDependencies(this);


            var cache = IoCManager.Resolve<IResourceCache>();
            _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 16);
        }

        public Vector2 GetCenter(IMapGrid grid, TileRef tileRef)
        {
            var gridXform = _componentManager.GetComponent<ITransformComponent>(grid.GridEntityId);

            return gridXform.WorldMatrix.Transform((Vector2) tileRef.GridIndices + 0.5f);
        }

        public Box2 GetBounds(IMapGrid grid, TileRef tileRef) => Box2.UnitCentered.Translated(GetCenter(grid, tileRef));
        //return new Box2Rotated(Box2.UnitCentered.Translated(center), -gridXform.WorldRotation, center);


        /// <inheritdoc />
        protected override void Draw(in OverlayDrawArgs args)
        {
            var playerEntity = _playerManager.LocalPlayer?.ControlledEntity;

            if (playerEntity == null)
                return;

            if (!playerEntity.TryGetComponent(out ExplosionOverlayComponent? explosion) ||
                explosion.ExplosionData == null ||
                explosion.GridData == null)
                return;

            var id = playerEntity.Transform.GridID;
            if (!_mapManager.TryGetGrid(id, out var grid))
                return;

            var tileRef = grid.GetTileRef(playerEntity.Transform.Coordinates);

            switch (args.Space)
            {
                case OverlaySpace.ScreenSpace:
                    DrawScreen(args, grid, tileRef, explosion.ExplosionData.Count);
                    break;
                case OverlaySpace.WorldSpace:
                    DrawWorld(args, grid, tileRef, explosion.ExplosionData.Count);
                    break;
            }

        }

        private void DrawScreen(OverlayDrawArgs args, IMapGrid grid, TileRef tile, int strength)
        {
            var handle = args.ScreenHandle;
            DrawTile(handle, grid, tile, strength);
        }

        private void DrawWorld(in OverlayDrawArgs args, IMapGrid grid, TileRef tile, int strength)
        {
            var handle = args.WorldHandle;
            DrawTile(handle, grid, tile, strength);
        }


        private void DrawTile(DrawingHandleWorld handle, IMapGrid grid, TileRef tile, int strength)
        {
            var bb = GetBounds(grid, tile);
            var color = ColorMap(strength);

            handle.DrawRect(bb, color, false);
            color.A = 0.4f;
            handle.DrawRect(bb, color);
        }

        private void DrawTile(DrawingHandleScreen handle, IMapGrid grid, TileRef tile, int strength)
        {
            var coords = _eyeManager.WorldToScreen(GetCenter(grid, tile));

            if (strength > 9)
                coords += (-16, -16);
            else
                coords += (-8, -16);

            handle.DrawString(_font, coords, strength.ToString());
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
