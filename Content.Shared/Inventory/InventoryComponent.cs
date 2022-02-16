using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Inventory;

public abstract class InventoryComponent : Component
{
    [DataField("templateId", required: true,
        customTypeSerializer: typeof(PrototypeIdSerializer<InventoryTemplatePrototype>))]
    public string TemplateId { get; } = "human";
}
