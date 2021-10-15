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
        ///     This delays the disappearance of the explosion after it has been fully drawn/expanded, so that it stays on the screen a little bit longer.
        ///     This is basically "padding" the radius, so that it stays on screen for Persistence*TimePerTile extra seconds
        /// </summary>
        public const float Persistence = 5;

        private readonly List<IEntity> _explosionLightSources = new();

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<ExplosionEvent>(HandleExplosionOverlay);
            SubscribeNetworkEvent<ExplosionUpdateEvent>(HandleExplosionUpdate);

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            _overlay = new ExplosionOverlay();
            if (!overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.AddOverlay(_overlay);
        }

        private void HandleExplosionUpdate(ExplosionUpdateEvent args)
        {
            var total = _overlay.ExplosionIndices.Count;
            for (int i = 1; i <= total; i++)
            {
                if (args.TileIndex > _overlay.Explosions[^i].Tiles.Count + Persistence)
                {
                    _overlay.ExplosionIndices.RemoveAt(total - i);
                    _overlay.Explosions.RemoveAt(total - i);
                    EntityManager.QueueDeleteEntity(_explosionLightSources[total-i]);
                    _explosionLightSources.RemoveAt(total-i);
                    continue;
                }
                _overlay.ExplosionIndices[^i] = args.TileIndex;
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
