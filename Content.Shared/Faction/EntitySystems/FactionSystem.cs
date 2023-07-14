namespace Content.Shared.Faction.EntitySystems;

public abstract partial class FactionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        InitializeEquipment();
        InitializeViewership();
        InitializeMembership();
        InitializeNetworking();
    }

    public override void Update(float frameTime)
    {
        UpdateNetworking();
    }
}
