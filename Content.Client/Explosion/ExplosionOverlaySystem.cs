using Content.Shared.Explosion;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using System.Collections.Generic;

namespace Content.Client.Explosion
{
    public sealed class ExplosionOverlaySystem : EntitySystem
    {
        private ExplosionOverlay _overlay = default!;

        /// <summary>
        ///     For how many seconds should an explosion stay on-screen once it has finished expanding?
        /// </summary>
        public const float ExplosionPersistence = 0.2f;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeNetworkEvent<ExplosionEvent>(OnExplosion);
            SubscribeNetworkEvent<ExplosionOverlayUpdateEvent>(HandleExplosionUpdate);

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            _overlay = new ExplosionOverlay();
            if (!overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.AddOverlay(_overlay);
        }

        public override void FrameUpdate(float frameTime)
        {
            base.FrameUpdate(frameTime);

            foreach (var explosion in _overlay.CompletedExplosions.ToArray())
            {
                explosion.Lifetime += frameTime;

                if (explosion.Lifetime >= ExplosionPersistence)
                {
                    EntityManager.QueueDeleteEntity(explosion.LightEntity);
                    _overlay.CompletedExplosions.Remove(explosion);
                }
            }
        }

        private void HandleExplosionUpdate(ExplosionOverlayUpdateEvent args)
        {
            if (_overlay.ActiveExplosion == null)
                return;

            _overlay.Index = args.Index;
            if (_overlay.Index <= _overlay.ActiveExplosion.Tiles.Count)
                return;

            // the explosion has finished expanding
            _overlay.Index = 0;
            _overlay.CompletedExplosions.Add(_overlay.ActiveExplosion);
            _overlay.ActiveExplosion = null;
        }

        private void OnExplosion(ExplosionEvent args)
        {
            var light = EntityManager.SpawnEntity("ExplosionLight", args.Epicenter);
            if (_overlay.ActiveExplosion != null)
                _overlay.CompletedExplosions.Add(_overlay.ActiveExplosion);

            _overlay.ActiveExplosion = new(args, light);
        }

        public override void Shutdown()
        {
            base.Shutdown();

            var overlayManager = IoCManager.Resolve<IOverlayManager>();
            if (overlayManager.HasOverlay<ExplosionOverlay>())
                overlayManager.RemoveOverlay<ExplosionOverlay>();
        }
    }

    internal class Explosion
    {
        public List<Texture[]> Frames = new();
        public IMapGrid Grid;
        public List<HashSet<Vector2i>> Tiles;
        public List<float> Intensity;
        public EntityUid LightEntity;

        /// <summary>
        ///     How long we have been drawing this explosion, starting from the time it was completed/full drawn.
        /// </summary>
        public float Lifetime;

        internal Explosion(ExplosionEvent args, IEntity light)
        {
            Tiles = args.Tiles;
            Intensity = args.Intensity;
            Grid = IoCManager.Resolve<IMapManager>().GetGrid(args.GridId);

            if (!IoCManager.Resolve<IPrototypeManager>().TryIndex(args.TypeID, out ExplosionPrototype? type))
                return;

            LightEntity = light.Uid;
            var lightComp = light.GetComponent<PointLightComponent>();
            lightComp.Radius = args.Tiles.Count;
            lightComp.Energy = lightComp.Radius;
            lightComp.Color = type.LightColor;

            var resource = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>(type.TexturePath).RSI;
            foreach (var state in resource)
            {
                Frames.Add(state.GetFrames(RSI.State.Direction.South));
            }
        }
    }
}
