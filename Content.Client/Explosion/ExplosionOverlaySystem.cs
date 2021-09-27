using Content.Shared.Explosion;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Client.Explosion
{
    public sealed class ExplosionOverlaySystem : EntitySystem
    {
        private ExplosionOverlay _overlay = default!;

        /// <summary>
        ///     Determines how quickly the visual explosion effect expands, in seconds per tile.
        /// </summary>
        public const float TimePerTile = 0.2f;

        private float _accumulatedFrameTime;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<ExplosionEvent>(HandleExplosionOverlay);

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            _overlay = new ExplosionOverlay();
            if (!overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.AddOverlay(_overlay);
        }

        /// <summary>
        ///     Process explosion animations;
        /// </summary>
        /// <param name="frameTime"></param>
        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            if (_overlay.Explosions.Count == 0)
                return;

            _accumulatedFrameTime += frameTime;

            if (_accumulatedFrameTime < TimePerTile)
                return;

            _accumulatedFrameTime -= TimePerTile;

            foreach (var explosion in _overlay.Explosions.ToArray())
            {
                explosion.Tiles.RemoveAt(explosion.Tiles.Count);
                explosion.Intensity.RemoveAt(explosion.Intensity.Count);

                if (explosion.Tiles.Count == 0)
                    _overlay.Explosions.Remove(explosion);
            }
        }

        private void HandleExplosionOverlay(ExplosionEvent args) => _overlay.Explosions.Add(args);

        public override void Shutdown()
        {
            base.Shutdown();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.RemoveOverlay<ExplosionOverlay>();
        }
    }
}
