using System;
using System.Collections.Generic;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Prototypes;

namespace Content.Server.Explosion
{
    /// <inheritdoc />
    public class ExplosionDrawing
    {
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntityLookup _lookup = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IInputManager _inputManager = default!;

        private bool _debugExplosions;


        /// <inheritdoc />
        public bool DebugExplosions
        {
            get => _debugExplosions;
            set
            {
                if (value == _debugExplosions)
                {
                    return;
                }

                _debugExplosions = value;

                if (value && !_overlayManager.HasOverlay<ExplosionOverlay>())
                {
                    _overlayManager.AddOverlay(new ExplosionOverlay(_lookup, _eyeManager));
                }
                else
                {
                    _overlayManager.RemoveOverlay<ExplosionOverlay>();
                }
            }
        }

        private class ExplosionOverlay : Overlay
        {
            private readonly IEyeManager _eyeManager;
            private readonly IMapManager _mapManager;
            private readonly IInputManager _inputManager;

            public override OverlaySpace Space => OverlaySpace.WorldSpace | OverlaySpace.ScreenSpace;
            private readonly ShaderInstance _shader;
            private readonly Font _font;

            private Vector2 _hoverStartScreen = Vector2.Zero;
            private List<IPhysBody> _hoverBodies = new();


            public ExplosionOverlay(IEyeManager eyeMan, IPrototypeManager protoMan, IInputManager inputManager, IMapManager mapManager)
            {
                _eyeManager = eyeMan;
                _inputManager = inputManager;
                _mapManager = mapManager;

                _shader = protoMan.Index<ShaderPrototype>("unshaded").Instance();
                var cache = IoCManager.Resolve<IResourceCache>();
                _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 10);
            }

            /// <inheritdoc />
            protected internal override void Draw(in OverlayDrawArgs args)
            {
                switch (args.Space)
                {
                    case OverlaySpace.ScreenSpace:
                        DrawScreen(args);
                        break;
                    case OverlaySpace.WorldSpace:
                        DrawWorld(args);
                        break;
                }

            }

            private void DrawScreen(in OverlayDrawArgs args)
            {
                var screenHandle = args.ScreenHandle;
                var lineHeight = _font.GetLineHeight(1f);
                Vector2 drawPos = _hoverStartScreen + new Vector2(20, 0) + new Vector2(0, -(_hoverBodies.Count * 4 * lineHeight / 2f));
                int row = 0;

                foreach (var body in _hoverBodies)
                {
                    if (body != _hoverBodies[0])
                    {
                        screenHandle.DrawString(_font, drawPos + new Vector2(0, row * lineHeight), "------");
                        row++;
                    }

                    screenHandle.DrawString(_font, drawPos + new Vector2(0, row * lineHeight), $"Ent: {body.Owner}");
                    row++;
                    screenHandle.DrawString(_font, drawPos + new Vector2(0, row * lineHeight), $"Layer: {Convert.ToString(body.CollisionLayer, 2)}");
                    row++;
                    screenHandle.DrawString(_font, drawPos + new Vector2(0, row * lineHeight), $"Mask: {Convert.ToString(body.CollisionMask, 2)}");
                    row++;
                    screenHandle.DrawString(_font, drawPos + new Vector2(0, row * lineHeight), $"Enabled: {body.CanCollide}, Hard: {body.Hard}, Anchored: {(body).BodyType == BodyType.Static}");
                    row++;
                }

            }

            private void DrawWorld(in OverlayDrawArgs args)
            {
                var worldHandle = args.WorldHandle;
                worldHandle.UseShader(_shader);
                var drawing = new PhysDrawingAdapter(worldHandle);

                _hoverBodies.Clear();
                var mouseScreenPos = _inputManager.MouseScreenPosition;
                var mouseWorldPos = _eyeManager.ScreenToMap(mouseScreenPos).Position;
                _hoverStartScreen = mouseScreenPos.Position;

                var viewport = _eyeManager.GetWorldViewport();
                var viewBounds = _eyeManager.GetWorldViewbounds();

                if (viewport.IsEmpty()) return;

                var mapId = _eyeManager.CurrentMap;
                var colorEdge = Color.Red.WithAlpha(0.33f);
                var drawnJoints = new HashSet<Joint>();

                foreach (var physBody in EntitySystem.Get<SharedBroadphaseSystem>().GetCollidingEntities(mapId, viewBounds))
                {
                    if (physBody.Owner.HasComponent<MapGridComponent>()) continue;

                    // all entities have a TransformComponent
                    var transform = physBody.Owner.Transform;

                    var worldBox = physBody.GetWorldAABB();
                    if (worldBox.IsEmpty()) continue;

                    var pTransform = physBody.GetTransform();

                    foreach (var fixture in physBody.Fixtures)
                    {
                        var shape = fixture.Shape;
                        var sleepPercent = physBody.Awake ? 0.0f : 1.0f;
                        shape.DebugDraw(drawing, transform.WorldMatrix, in viewport, sleepPercent);

                        drawing.SetTransform(in Matrix3.Identity);

                        var aabb = shape.ComputeAABB(pTransform, 0);
                        worldHandle.DrawRect(aabb, Color.Blue, false);
                    }

                    foreach (var joint in physBody.Joints)
                    {
                        if (drawnJoints.Contains(joint)) continue;
                        drawnJoints.Add(joint);

                        joint.DebugDraw(drawing, in viewport);
                        drawing.SetTransform(in Matrix3.Identity);
                    }

                    if (worldBox.Contains(mouseWorldPos))
                    {
                        _hoverBodies.Add(physBody);
                    }

                    // draw AABB
                    worldHandle.DrawRect(worldBox, colorEdge, false);
                }
            }

            private class PhysDrawingAdapter : DebugDrawingHandle
            {
                private readonly DrawingHandleWorld _handle;

                public PhysDrawingAdapter(DrawingHandleWorld worldHandle)
                {
                    _handle = worldHandle;
                }

                public override Color WakeMixColor => Color.White;
                public override Color GridFillColor => Color.Blue.WithAlpha(0.05f);
                public override Color RectFillColor => Color.Green.WithAlpha(0.25f);

                public override Color CalcWakeColor(Color color, float wakePercent)
                {
                    var percent = MathHelper.Clamp(wakePercent, 0, 1);

                    var r = 1 - (percent * (1 - color.R));
                    var g = 1 - (percent * (1 - color.G));
                    var b = 1 - (percent * (1 - color.B));

                    return new Color(r, g, b, color.A);
                }

                public override void DrawRect(in Box2 box, in Color color)
                {
                    _handle.DrawRect(box, color);
                }

                public override void DrawRect(in Box2Rotated box, in Color color)
                {
                    _handle.DrawRect(box, color);
                }

                public override void DrawCircle(Vector2 origin, float radius, in Color color)
                {
                    _handle.DrawCircle(origin, radius, color);
                }

                public override void DrawPolygonShape(Vector2[] vertices, in Color color)
                {
                    _handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, vertices, color);
                }

                public override void DrawLine(Vector2 start, Vector2 end, in Color color)
                {
                    _handle.DrawLine(start, end, color);
                }

                public override void SetTransform(in Matrix3 transform)
                {
                    _handle.SetTransform(transform);
                }


            }
        }
    }
}
