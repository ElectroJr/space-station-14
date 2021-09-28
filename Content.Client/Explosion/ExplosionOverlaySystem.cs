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
        public const float TimePerTile = 0.03f;

        /// <summary>
        ///     This delays the disappearance of the explosion after it has been fully drawn/expanded, so that it stays on the screen a little bit longer.
        ///     This is basically "padding" the radius, so that it stays on screen for Persistence*TimePerTile extra seconds
        /// </summary>
        public const float Persistence = 15;

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

            for (var i = 0; i < _overlay.Explosions.Count; i++)
            {
                _overlay.ExplosionIndices[i]++;

                if (_overlay.ExplosionIndices[i] > _overlay.Explosions[i].Tiles.Count + Persistence)
                {
                    _overlay.Explosions.RemoveAt(i);
                    _overlay.ExplosionIndices.RemoveAt(i);
                }
            }
        }

        private void HandleExplosionOverlay(ExplosionEvent args)
        {
            _overlay.Explosions.Add(args);
            _overlay.ExplosionIndices.Add(4);
        }

        public override void Shutdown()
        {
            base.Shutdown();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.RemoveOverlay<ExplosionOverlay>();
        }
    }
}
