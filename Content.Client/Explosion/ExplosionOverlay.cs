using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Random;
using System;
using System.Collections.Generic;

namespace Content.Client.Explosion;

[UsedImplicitly]
public sealed class ExplosionOverlay : Overlay
{
    /// <summary>
    ///     The explosion that needs to be drawn. This explosion is currently being processed by the server and
    ///     expanding outwards.
    /// </summary>
    internal Explosion? ActiveExplosion;

    /// <summary>
    ///     This index specifies what parts of the currently expanding explosion should be drawn.
    /// </summary>
    public int Index;

    /// <summary>
    ///     These explosions have finished expanding, but we will draw for a few more frames. This is important for
    ///     small explosions, as otherwise they disappear far too quickly.
    /// </summary>
    internal List<Explosion> CompletedExplosions = new ();

    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    /// <summary>
    ///     How intense does the explosion have to be at a tile to advance to the next fire texture state?
    /// </summary>
    public const int IntensityPerState = 12;

    public ExplosionOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var drawHandle = args.WorldHandle;

        if (ActiveExplosion != null)
        {
            DrawExplosion(drawHandle, args.WorldBounds, ActiveExplosion, Index);
        }

        foreach (var exp in CompletedExplosions)
        {
            DrawExplosion(drawHandle, args.WorldBounds, exp, exp.Intensity.Count);
        }

        drawHandle.SetTransform(Matrix3.Identity);
    }

    private void DrawExplosion(DrawingHandleWorld drawHandle, Box2Rotated worldBounds, Explosion exp, int index)
    {
        if (exp.Map != _eyeManager.CurrentMap)
            return;

        Box2 gridBounds;
        Matrix3 worldMatrix;
        foreach (var (gridId, tileSetList) in exp.Tiles)
        {
            if (gridId.IsValid())
            {
                if (!_mapManager.TryGetGrid(gridId, out var grid))
                    continue;

                gridBounds = grid.InvWorldMatrix.TransformBox(worldBounds);
                worldMatrix = grid.WorldMatrix;
            }
            else
            {
                gridBounds = Matrix3.Invert(exp.SpaceMatrix).TransformBox(worldBounds);
                worldMatrix = exp.SpaceMatrix;
            }
            
            drawHandle.SetTransform(worldMatrix);

            for (var j = 0; j < index; j++)
            {
                if (!tileSetList.TryGetValue(j, out var tiles)) continue;

                var frameIndex = (int) Math.Min(exp.Intensity[j] / IntensityPerState, exp.FireFrames.Count - 1);
                var frames = exp.FireFrames[frameIndex];


                foreach (var tile in tiles)
                {
                    Vector2 bottomLeft = (tile.X, tile.Y);

                    if (!gridBounds.Contains((bottomLeft + 0.5f)*1f))
                        continue;

                    var texture = _robustRandom.Pick(frames);
                    drawHandle.DrawTexture(texture, bottomLeft, exp.FireColor);
                }
            }
        }
    }
}
