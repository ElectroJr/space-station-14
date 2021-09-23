using Content.Shared.Explosion;
using Content.Shared.Input;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Players;

namespace Content.Server.Explosion
{
    [NetworkedComponent()]
    [RegisterComponent]
    [ComponentReference(typeof(SharedExplosionOverlayComponent))]
    public class ExplosionOverlayComponent : SharedExplosionOverlayComponent
    {

        private int _strength;
        private int _damage;

        protected override void Initialize()
        {
            base.Initialize();
            Dirty();

            CommandBinds.Builder
            .Bind(ContentKeyFunctions.IncreaseStrength,
                new PointerInputCmdHandler(HandleIncreaseStrength))
            .Bind(ContentKeyFunctions.DecreaseStrength,
                new PointerInputCmdHandler(HandleDecreaseStrength))
            .Bind(ContentKeyFunctions.IncreaseDamage,
                new PointerInputCmdHandler(HandleIncreaseDamage))
            .Bind(ContentKeyFunctions.DecreaseDamage,
                new PointerInputCmdHandler(HandleDecreaseDamage))
            .Register<ExplosionOverlayComponent>();
        }

        private void Update()
        {
            ExplosionData = EntitySystem.Get<ExplosionSystem>().SpawnExplosion(Owner.Transform.MapPosition, _strength, _damage);
            GridData = Owner.Transform.GridID;
            Dirty();
        }

        private bool HandleDecreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _strength--;
            Update();
            return true;
        }

        private bool HandleIncreaseStrength(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _strength++;
            Update();
            return true;
        }

        private bool HandleIncreaseDamage(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _damage--;
            Update();
            return true;
        }

        private bool HandleDecreaseDamage(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
        {
            _damage++;
            Update();
            return true;
        }
    }
}
