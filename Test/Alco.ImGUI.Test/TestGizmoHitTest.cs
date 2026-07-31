using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// U4 and U10: axis/plane/ring hit-testing with a synthetic camera and injected
/// mouse, plus the behind-camera early-out.
/// </summary>
[TestFixture]
public class TestGizmoHitTest
{
    private const GizmoOperation AllOps = GizmoOperation.Translate | GizmoOperation.Rotate | GizmoOperation.RotateScreen
        | GizmoOperation.Scale | GizmoOperation.ScaleUniform;

    private Matrix4x4 _view;
    private Matrix4x4 _projection;
    private GizmoContext _ctx = null!;
    private Matrix4x4 _model;

    [SetUp]
    public void SetUp()
    {
        (_view, _projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        _ctx = CreateContext();
        _model = Matrix4x4.Identity;
        // Settle frame far from any handle so ScreenFactor and the hover state are computed.
        Frame(_ctx, new Vector2(5f, 5f), false, _view, _projection, AllOps, GizmoMode.World, ref _model);
    }

    private void Hover(Vector2 mouse, GizmoOperation operation = AllOps)
    {
        Frame(_ctx, mouse, false, _view, _projection, operation, GizmoMode.World, ref _model);
    }

    [Test]
    public void U4_AxisHit_WithinThreshold()
    {
        float sf = _ctx.ScreenFactor;
        Hover(ToScreen(new Vector3(0.55f * sf, 0f, 0f), _view, _projection));
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveX));
    }

    [Test]
    public void U4_AxisHit_OutsideThreshold()
    {
        float sf = _ctx.ScreenFactor;
        Vector2 onAxis = ToScreen(new Vector3(0.55f * sf, 0f, 0f), _view, _projection);
        Hover(onAxis + new Vector2(0f, 30f));
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.None));
    }

    [Test]
    public void U4_PlaneHit_WithinQuad()
    {
        float sf = _ctx.ScreenFactor;
        Hover(ToScreen(new Vector3(0.65f * sf, 0.65f * sf, 0f), _view, _projection));
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveXY));
    }

    [Test]
    public void U4_PlaneHit_OutsideQuad()
    {
        float sf = _ctx.ScreenFactor;
        // Beyond the 0.8 quad extent, but inside the ring-free zone and away from both axes.
        Hover(ToScreen(new Vector3(0.95f * sf, 0.95f * sf, 0f), _view, _projection));
        Assert.That(_ctx.FrameHoverType, Is.Not.EqualTo(GizmoMoveType.MoveXY));
    }

    [Test]
    public void U4_RingHit_WithinThreshold()
    {
        float sf = _ctx.ScreenFactor;
        float angle = MathF.PI / 4f;
        Vector3 onRing = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * (1.2f * sf);
        Hover(ToScreen(onRing, _view, _projection));
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.RotateZ));
    }

    [Test]
    public void U4_RingHit_OutsideThreshold()
    {
        float sf = _ctx.ScreenFactor;
        float angle = MathF.PI / 4f;
        Vector2 onRing = ToScreen(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * (1.2f * sf), _view, _projection);
        Vector2 center = ToScreen(Vector3.Zero, _view, _projection);
        Vector2 outward = Vector2.Normalize(onRing - center) * 30f;
        Hover(onRing + outward);
        Assert.That(_ctx.FrameHoverType, Is.Not.EqualTo(GizmoMoveType.RotateZ));
    }

    [Test]
    public void U4_CenterHit_ScreenMove()
    {
        Hover(ToScreen(Vector3.Zero, _view, _projection), GizmoOperation.Translate);
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveScreen));
    }

    [Test]
    public void U4_ScaleAxisHit()
    {
        float sf = _ctx.ScreenFactor;
        Hover(ToScreen(new Vector3(0.55f * sf, 0f, 0f), _view, _projection), GizmoOperation.ScaleX);
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.ScaleX));
    }

    [Test]
    public void U4_UniformScaleRingHit()
    {
        Vector2 center = ToScreen(Vector3.Zero, _view, _projection);
        Hover(center + new Vector2(20f, 0f), GizmoOperation.ScaleUniform);
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.ScaleXYZ));
    }

    [Test]
    public void U4_MouseOutsideViewport_NoHit()
    {
        float sf = _ctx.ScreenFactor;
        Vector2 onAxis = ToScreen(new Vector3(0.55f * sf, 0f, 0f), _view, _projection);
        // Shift the whole thing outside the viewport: same relative position, but the rect rejects it.
        Assert.That(Viewport.Contains(onAxis), Is.True);
        Hover(new Vector2(-50f, onAxis.Y));
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.None));
    }

    [Test]
    public void U4_DisabledOperationBits_NoHit()
    {
        float sf = _ctx.ScreenFactor;
        // TranslateY only: the X axis must not be hittable.
        Hover(ToScreen(new Vector3(0.55f * sf, 0f, 0f), _view, _projection), GizmoOperation.TranslateY);
        Assert.That(_ctx.FrameHoverType, Is.Not.EqualTo(GizmoMoveType.MoveX));
    }

    [Test]
    public void U10_BehindCamera_ReturnsFalseAndStaysIdle()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.CreateTranslation(0f, 0f, -20f);

        bool manipulated = Frame(ctx, new Vector2(400f, 300f), false, view, projection, AllOps, GizmoMode.World, ref model);
        Assert.That(manipulated, Is.False);
        Assert.That(ctx.CallValid, Is.False);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.None));

        // Pressing must not activate a drag.
        manipulated = Frame(ctx, new Vector2(400f, 300f), true, view, projection, AllOps, GizmoMode.World, ref model);
        Assert.That(manipulated, Is.False);
        Assert.That(ctx.Using, Is.False);
    }

    [Test]
    public void U10_FarOutsideFrustum_NotHittable()
    {
        Matrix4x4 model = Matrix4x4.CreateTranslation(1000f, 0f, 0f);
        bool manipulated = Frame(_ctx, new Vector2(400f, 300f), false, _view, _projection, AllOps, GizmoMode.World, ref model);
        Assert.That(manipulated, Is.False);
        Assert.That(_ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.None));
    }
}
