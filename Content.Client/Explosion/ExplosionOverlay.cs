using System.Linq;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
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

        // TODO: When worldHandle can do DrawCircle change this.
        public override OverlaySpace Space => OverlaySpace.ScreenSpace;

        public ExplosionOverlay()
        {
            IoCManager.InjectDependencies(this);
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            // PVS should control the overlay pretty well so the overlay doesn't get instantiated unless we're near one...
            var playerEntity = _playerManager.LocalPlayer?.ControlledEntity;

            if (playerEntity == null)
            {
                return;
            }

            var elapsedTime = (float) (_gameTiming.CurTime - _lastTick).TotalSeconds;
            _lastTick = _gameTiming.CurTime;

            var radiationPulses = _componentManager
                .EntityQuery<RadiationPulseComponent>(true)
                .ToList();

            var screenHandle = args.ScreenHandle;
            var viewport = _eyeManager.GetWorldViewport();

            foreach (var grid in _mapManager.FindGridsIntersecting(playerEntity.Transform.MapID, viewport))
            {
                foreach (var pulse in radiationPulses)
                {
                    if (!pulse.Draw || grid.Index != pulse.Owner.Transform.GridID) continue;

                    // TODO: Check if viewport intersects circle
                    var circlePosition = args.ViewportControl!.WorldToScreen(pulse.Owner.Transform.WorldPosition);

                    // change to worldhandle when implemented
                    screenHandle.DrawCircle(
                        circlePosition,
                        pulse.Range * 64,
                        GetColor(pulse.Owner, pulse.Decay ? elapsedTime : 0, pulse.EndTime));
                }
            }
        }
    }
}
