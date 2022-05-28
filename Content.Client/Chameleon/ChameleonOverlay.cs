using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Chameleon;

/// <summary>
///     Simple overlay that simply draws to the screen using some shader.
/// </summary>
public sealed class ChameleonOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    public ChameleonOverlay(IPrototypeManager protoMan)
    {
        // Set up overlay shader
        _shader = protoMan.Index<ShaderPrototype>("Chameleon").InstanceUnique();
        _shader.StencilRef = ChameleonSystem.ChameleonStencilRef;
        _shader.StencilReadMask = ChameleonSystem.ChameleonStencilRef;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null || ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        args.DrawingHandle.SetTransform(Matrix3.Identity);

        // If distortion wave is relative to screen coords, then chameleon effect is obvious whenever something moves on screen.
        // --> if player moves, eveything on screen moves --> easy chameleon detector.

        // So we're having the chameleon effect be relative to the world position
        // but that means the effect breaks whenever the  grid is moving.... uhhh...

        var frame = args.Viewport.WorldToLocal(args.Viewport.Eye.Position.Position);
        frame.Y = args.Viewport.Size.Y - frame.Y;
        _shader.SetParameter("reference_frame", frame);

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(args.WorldAABB, Color.White);
        worldHandle.UseShader(null);
    }
}
