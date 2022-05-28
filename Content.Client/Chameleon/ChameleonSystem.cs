using Content.Client.Interactable.Components;
using Content.Client.GenericOverlay;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Chameleon;

public sealed class ChameleonSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;


    public static ushort ChameleonStencilRef = 0xCA; // CAmmo?
    private ShaderInstance _stencilShader = default!;
    private GenericShaderOverlay _overlay = default!;

    public override void Initialize()
    {
        // Set up stencil shader
        _stencilShader = _protoMan.Index<ShaderPrototype>("StencilMask").InstanceUnique();
        _stencilShader.StencilRef = ChameleonStencilRef;
        _stencilShader.MakeImmutable();

        // Set up overlay shader
        var shader = _protoMan.Index<ShaderPrototype>("Chameleon").InstanceUnique();
        shader.StencilRef = ChameleonStencilRef;

        _overlay = new(OverlaySpace.WorldSpace, shader, true);
        _overlayMan.AddOverlay(_overlay);

        SubscribeLocalEvent<ChameleonComponent, ComponentInit>(OnAdd);
        SubscribeLocalEvent<ChameleonComponent, ComponentRemove>(OnRemove);
    }

    public override void Shutdown()
    {
        _overlayMan.RemoveOverlay(_overlay);
    }

    private void OnRemove(EntityUid uid, ChameleonComponent component, ComponentRemove args)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        sprite.PostShader = null;

        if (component.HadOutline)
            AddComp<InteractionOutlineComponent>(uid);
    }

    private void OnAdd(EntityUid uid, ChameleonComponent component, ComponentInit args)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        sprite.PostShader = _stencilShader;

        if (TryComp(uid, out InteractionOutlineComponent? outline))
        {
            RemComp(uid, outline);
            component.HadOutline = true;
        }
    }
}
