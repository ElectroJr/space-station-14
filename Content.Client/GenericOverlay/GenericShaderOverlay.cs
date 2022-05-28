using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client.GenericOverlay;

/// <summary>
///     Simple overlay that simply draws to the screen using some shader.
/// </summary>
public sealed class GenericShaderOverlay : Overlay
{
    public override OverlaySpace Space => _space;
    public override bool RequestScreenTexture => true;

    private readonly OverlaySpace _space;
    private readonly ShaderInstance _shader;
    private readonly bool _requestScreenTexture;

    public GenericShaderOverlay(OverlaySpace space, ShaderInstance shader, bool requestScreenTexture = false)
    {
        _space = space;
        _shader = shader;
        _requestScreenTexture = requestScreenTexture;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye == null)
            return;

        if (_requestScreenTexture)
        {
            if (ScreenTexture == null)
                return;

            _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        }

        args.DrawingHandle.SetTransform(Matrix3.Identity);

        if (args.Space == OverlaySpace.ScreenSpace)
        {
            var screenHandle = args.ScreenHandle;
            screenHandle.UseShader(_shader);
            screenHandle.DrawRect(args.ViewportBounds, Color.White);
            screenHandle.UseShader(null);
            return;
        }

        var worldHandle = args.WorldHandle;
        worldHandle.UseShader(_shader);
        worldHandle.DrawRect(args.WorldAABB, Color.White);
        worldHandle.UseShader(null);
    }
}
