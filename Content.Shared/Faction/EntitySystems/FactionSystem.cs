using Robust.Shared.GameStates;

namespace Content.Shared.Faction;

/// <summary>
///     This component stores the factions that an entity is a member of.
/// </summary>
public abstract partial class FactionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        InitializeNetworking();
        InitializeEquipment();
        InitializeViewership();
        InitializeMembership();
    }

    public override void Update(float frameTime)
    {
        UpdateNetworking();  
    }
}
