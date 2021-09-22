using System.Linq;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client.Explosion
{
    [UsedImplicitly]
    public sealed class ExplosionOverlay : Overlay
    {
        [Dependency] private readonly IComponentManager _componentManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;

        public override OverlaySpace Space => OverlaySpace.WorldSpace;

        public ExplosionOverlay()
        {
            IoCManager.InjectDependencies(this);
        }


        public Box2 GetBounds(TileRef tileRef)
        {
            var grid = _mapManager.GetGrid(tileRef.GridIndex);
            var gridXform = _componentManager.GetComponent<ITransformComponent>(grid.GridEntityId);

            var center = gridXform.WorldMatrix.Transform((Vector2) tileRef.GridIndices + 0.5f);
            return Box2.UnitCentered.Translated(center);
            //return new Box2Rotated(Box2.UnitCentered.Translated(center), -gridXform.WorldRotation, center);
        }


        protected override void Draw(in OverlayDrawArgs args)
        {
            var handle = args.WorldHandle;

            var playerEntity = _playerManager.LocalPlayer?.ControlledEntity;

            if (playerEntity == null)
            {
                return;
            }

            var id = playerEntity.Transform.GridID;
            if (!_mapManager.TryGetGrid(id, out var grid))
                return;

            var tileRef = grid.GetTileRef(playerEntity.Transform.Coordinates);

            handle.DrawRect(GetBounds(tileRef), Color.FromHex("#f00f"), filled: false);
            handle.DrawRect(GetBounds(tileRef), Color.FromHex("#f005"), filled: false);
        }
    }
}
