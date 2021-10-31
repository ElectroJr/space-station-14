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
    /// <summary>
    ///     This system is responsible for showing the client-side explosion effects (light source & fire-overlay). The
    ///     fire overlay code is just a bastardized version of the atmos plasma fire overlay and uses the same texture.
    /// </summary>
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

            // increment the lifetime of completed explosions, and remove them if they have been ons screen for more
            // than ExplosionPersistence seconds
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

        /// <summary>
        ///     The server has processed some explosion. This updates the client-side overlay so that the area covered
        ///     by the fire-visual matches up with the area that the explosion has affected.
        /// </summary>
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

        /// <summary>
        ///     A new explosion occurred. This prepares the client-side light entity and stores the
        ///     explosion/fire-effect overlay data.
        /// </summary>
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
        public IMapGrid Grid;
        public List<HashSet<Vector2i>> Tiles;
        public List<float> Intensity;
        public EntityUid LightEntity;

        /// <summary>
        ///     How long have we been drawing this explosion, starting from the time the explosion was fully drawn.
        /// </summary>
        public float Lifetime;

        /// <summary>
        ///     The textures used for the explosion fire effect. Each fire-state is associated with an explosion
        ///     intensity range, and each stat itself has several textures.
        /// </summary>
        public List<Texture[]> Frames = new();

        /// <summary>
        ///     We want the first three states in Fire.rsi, and not the last two.
        /// </summary>
        private const int TotalFireStates = 3;

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

            var fireRsi = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>(type.TexturePath).RSI;
            foreach (var state in fireRsi)
            {
                Frames.Add(state.GetFrames(RSI.State.Direction.South));
                if (Frames.Count == TotalFireStates)
                    break;
            }
        }
    }
}
