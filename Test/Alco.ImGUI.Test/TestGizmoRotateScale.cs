using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// U7 and U8: 2D RotateZ angle/sign convention and scale solve direction/ratio.
/// </summary>
[TestFixture]
public class TestGizmoRotateScale
{
    [Test]
    public void U7_RotateZ_2D_CounterClockwiseDrag_DecreasesEngineAngle()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform2D transform = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        float ringRadius = 1.2f * sf;

        Vector2 grab = ToScreen(new Vector3(ringRadius, 0f, 0f), view, projection);
        Frame(ctx, grab, false, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.RotateZ));

        Assert.That(Frame(ctx, grab, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform),
            Is.False, "activation frame must not modify");

        // Drag 90 degrees counterclockwise on screen (world +X to world +Y).
        Vector2 target = ToScreen(new Vector3(0f, ringRadius, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);
        Assert.That(manipulated, Is.True);

        // Engine convention: a positive Rotation2D angle is clockwise on screen,
        // so a counterclockwise drag yields a negative angle.
        Assert.That(transform.Rotation.ToDegree(), Is.EqualTo(-90f).Within(1.0f));

        Frame(ctx, target, false, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);
        Assert.That(ctx.Using, Is.False);
    }

    [Test]
    public void U7_RotateZ_2D_ClockwiseDrag_IncreasesEngineAngle()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform2D transform = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        float ringRadius = 1.2f * sf;

        Vector2 grab = ToScreen(new Vector3(ringRadius, 0f, 0f), view, projection);
        Frame(ctx, grab, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);

        // Drag 45 degrees clockwise on screen (world +X to world -Y).
        Vector2 target = ToScreen(new Vector3(ringRadius * MathF.Cos(MathF.PI / 4f), -ringRadius * MathF.Sin(MathF.PI / 4f), 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref transform);
        Assert.That(manipulated, Is.True);
        Assert.That(transform.Rotation.ToDegree(), Is.EqualTo(45f).Within(1.0f));
    }

    [Test]
    public void U7_RotateZ_3D_Perspective()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        float ringRadius = 1.2f * sf;

        Vector2 grab = ToScreen(new Vector3(ringRadius, 0f, 0f), view, projection);
        Frame(ctx, grab, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref model);

        Vector2 target = ToScreen(new Vector3(0f, ringRadius, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, GizmoOperation.RotateZ, GizmoMode.World, ref model);
        Assert.That(manipulated, Is.True);

        // The matrix rotation is a +90 degree rotation about world Z (row-major: X rotates toward Y).
        Assert.That(GizmoMath.TryDecompose(model, out _, out Quaternion rotation, out _), Is.True);
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        RotationEquivalent(expected, rotation, 1e-2f);
    }

    [Test]
    public void U8_ScaleX_Perspective()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.ScaleX;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.Local, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, false, view, projection, op, GizmoMode.Local, ref model);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.ScaleX));

        Assert.That(Frame(ctx, grab, true, view, projection, op, GizmoMode.Local, ref model), Is.False,
            "activation frame must not modify");

        // Drag from 0.55 sf to 1.0 sf along the axis: ratio = 1 / 0.55.
        Vector2 target = ToScreen(new Vector3(1.0f * sf, 0f, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, op, GizmoMode.Local, ref model);
        Assert.That(manipulated, Is.True);

        Assert.That(GizmoMath.TryDecompose(model, out _, out _, out Vector3 scale), Is.True);
        Assert.That(scale.X, Is.EqualTo(1f / 0.55f).Within(0.05f));
        Assert.That(scale.Y, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(scale.Z, Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void U8_ScaleX_Shrink_WhenDraggingInward()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.ScaleX;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.Local, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, op, GizmoMode.Local, ref model);
        Vector2 target = ToScreen(new Vector3(0.3f * sf, 0f, 0f), view, projection);
        bool manipulated = Frame(ctx, target, true, view, projection, op, GizmoMode.Local, ref model);
        Assert.That(manipulated, Is.True);

        Assert.That(GizmoMath.TryDecompose(model, out _, out _, out Vector3 scale), Is.True);
        Assert.That(scale.X, Is.LessThan(1f));
        Assert.That(scale.X, Is.GreaterThan(0.001f));
    }

    [Test]
    public void U8_ScaleUniform_MouseX()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.ScaleUniform;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.Local, ref model);
        Vector2 center = ToScreen(Vector3.Zero, view, projection);
        Vector2 grab = center + new Vector2(20f, 0f);

        Frame(ctx, grab, false, view, projection, op, GizmoMode.Local, ref model);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.ScaleXYZ));

        Frame(ctx, grab, true, view, projection, op, GizmoMode.Local, ref model);
        // +50 px on mouse X: scaleDelta = 0.5 -> scale 1.5 on all axes.
        bool manipulated = Frame(ctx, grab + new Vector2(50f, 0f), true, view, projection, op, GizmoMode.Local, ref model);
        Assert.That(manipulated, Is.True);

        Assert.That(GizmoMath.TryDecompose(model, out _, out _, out Vector3 scale), Is.True);
        AreEqual(new Vector3(1.5f), scale, 0.01f);
    }
}
