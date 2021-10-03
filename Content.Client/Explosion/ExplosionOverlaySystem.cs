using Content.Shared.Explosion;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    public sealed class ExplosionOverlaySystem : EntitySystem
    {
        private ExplosionOverlay _overlay = default!;

        /// <summary>
        ///     Determines how quickly the visual explosion effect expands, in seconds per tile iteration.
        /// </summary>
        public const float TimePerTile = 0.08f;

        /// <summary>
        ///     This delays the disappearance of the explosion after it has been fully drawn/expanded, so that it stays on the screen a little bit longer.
        ///     This is basically "padding" the radius, so that it stays on screen for Persistence*TimePerTile extra seconds
        /// </summary>
        public const float Persistence = 15;

        private float _accumulatedFrameTime;

        private readonly List<IEntity> _explosionLightSources = new();

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

            while (_accumulatedFrameTime >= TimePerTile)
            {
                _accumulatedFrameTime -= TimePerTile;

                for (var i = 0; i < _overlay.Explosions.Count; i++)
                {
                    _overlay.ExplosionIndices[i]++;

                    if (_overlay.ExplosionIndices[i] > _overlay.Explosions[i].Tiles.Count + Persistence)
                    {
                        _overlay.Explosions.RemoveAt(i);
                        _overlay.ExplosionIndices.RemoveAt(i);
                        EntityManager.QueueDeleteEntity(_explosionLightSources[i]);
                        _explosionLightSources.RemoveAt(i);
                    }
                }
            }
        }

        private void HandleExplosionOverlay(ExplosionEvent args)
        {
            _overlay.Explosions.Add(args);
            _overlay.ExplosionIndices.Add(4);

            // Note that this is a SINGLE point source at the epicentre. for MOST purposes, this is good enough. but if
            // the explosion snakes around a corner, it will not light it up properly.

            // TODO EXPLOSION make the light source prototype defined by the explosion prototype
            var explosionLight = EntityManager.SpawnEntity("Explosion", args.Epicenter);
            var light = explosionLight.GetComponent<PointLightComponent>();
            light.Radius = args.Tiles.Count;
            light.Energy = light.Radius; // careful, don't look directly at the nuke.
            _explosionLightSources.Add(explosionLight);
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
