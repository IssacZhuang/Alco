using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// U9: snap behavior — translation snaps the cumulative displacement from drag
/// start, rotation snaps the cumulative angle, components &lt;= 0 are skipped.
/// </summary>
[TestFixture]
public class TestGizmoSnap
{
    [Test]
    public void U9_TranslationSnap_AppliesToCumulativeDelta()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        // Start off-grid: snapping the cumulative delta preserves the 0.3 offset.
        Transform2D transform = new Transform2D(new Vector2(0.3f, 0f), Rotation2D.Identity, Vector2.One);
        const GizmoOperation op = GizmoOperation.TranslateX | GizmoOperation.TranslateY;
        GizmoSnap snap = GizmoSnap.XY(1f, 1f);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.3f + 0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref transform, snap);
        // Drag 0.6 world units: cumulative delta 0.6 snaps to 1.0; final = start + 1.0.
        Vector2 target = ToScreen(new Vector3(0.3f + 0.55f * sf + 0.6f, 0f, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, op, GizmoMode.World, ref transform, snap);

        Assert.That(manipulated, Is.True);
        Assert.That(transform.Position.X, Is.EqualTo(1.3f).Within(0.05f), "cumulative delta must snap, keeping the start offset");
        Assert.That(transform.Position.X, Is.Not.EqualTo(1.0f).Within(0.1f), "absolute-position snapping would give 1.0");
        Assert.That(transform.Position.Y, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void U9_TranslationSnap_BelowHalfStep_KeepsPosition()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform2D transform = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);
        GizmoSnap snap = GizmoSnap.XY(1f, 1f);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.TranslateX, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, GizmoOperation.TranslateX, GizmoMode.World, ref transform, snap);
        Vector2 target = ToScreen(new Vector3(0.55f * sf + 0.4f, 0f, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, GizmoOperation.TranslateX, GizmoMode.World, ref transform, snap);

        // 0.4 of a 1.0 step snaps back to zero: no effective change.
        Assert.That(manipulated, Is.False);
        Assert.That(transform.Position.X, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void U9_TranslationSnap_NonPositiveComponentsSkipped()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform2D transform = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);
        // Snap only X; Y must stay free.
        GizmoSnap snap = new GizmoSnap(new Vector3(1f, 0f, 0f));

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.TranslateY, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0f, 0.55f * sf, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, GizmoOperation.TranslateY, GizmoMode.World, ref transform, snap);
        Vector2 target = ToScreen(new Vector3(0f, 0.55f * sf + 0.6f, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, GizmoOperation.TranslateY, GizmoMode.World, ref transform, snap);

        Assert.That(manipulated, Is.True);
        Assert.That(transform.Position.Y, Is.EqualTo(0.6f).Within(0.05f), "Y snap component is 0 and must not snap");
    }

    [Test]
    public void U9_RotationSnap_SnapsCumulativeAngle()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform2D transform = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);
        GizmoSnap snap = GizmoSnap.Uniform(0f, 15f);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        float ringRadius = 1.2f * sf;

        Vector2 grab = ToScreen(new Vector3(ringRadius, 0f, 0f), view, projection);
        Frame(ctx, grab, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform, snap);

        // Drag to +20 degrees: snaps to 15 degrees (engine sign: CCW is negative).
        float a20 = MathF.PI / 9f;
        Vector2 target = ToScreen(new Vector3(ringRadius * MathF.Cos(a20), ringRadius * MathF.Sin(a20), 0f), view, projection);
        Assert.That(Frame(ctx, target, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform, snap), Is.True);
        Assert.That(transform.Rotation.ToDegree(), Is.EqualTo(-15f).Within(0.5f));

        // Drag back to +6 degrees: below half the step, snaps to 0.
        float a6 = MathF.PI / 30f;
        Vector2 target2 = ToScreen(new Vector3(ringRadius * MathF.Cos(a6), ringRadius * MathF.Sin(a6), 0f), view, projection);
        Assert.That(Frame(ctx, target2, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform, snap), Is.True);
        Assert.That(transform.Rotation.ToDegree(), Is.EqualTo(0f).Within(0.5f));
    }

    [Test]
    public void U9_ScaleSnap_SnapsMultiplier()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.ScaleX;
        GizmoSnap snap = GizmoSnap.Uniform(0f, 0f, 0.5f);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.Local, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, op, GizmoMode.Local, ref model, snap);
        // Drag to ratio 1.3: snaps to 1.5.
        Vector2 target = ToScreen(new Vector3(0.55f * sf * 1.3f, 0f, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, op, GizmoMode.Local, ref model, snap);

        Assert.That(manipulated, Is.True);
        Assert.That(GizmoMath.TryDecompose(model, out _, out _, out Vector3 scale), Is.True);
        Assert.That(scale.X, Is.EqualTo(1.5f).Within(0.02f));
    }
}
