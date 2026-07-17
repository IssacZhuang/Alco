using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// U5 and U6: translation solve direction and sign across perspective/orthographic
/// cameras and Local/World modes, and the 2D overload's component isolation.
/// </summary>
[TestFixture]
public class TestGizmoTranslate
{
    [Test]
    public void U5_TranslateX_Perspective_World()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.TranslateX | GizmoOperation.TranslateY;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, false, view, projection, op, GizmoMode.World, ref model);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveX));

        Assert.That(Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref model), Is.False, "activation frame must not modify");
        Assert.That(ctx.Using, Is.True);

        // Drag right: world X increases, other axes untouched.
        bool manipulated = Frame(ctx, grab + new Vector2(100f, 0f), true, view, projection, op, GizmoMode.World, ref model);
        Assert.That(manipulated, Is.True);
        Assert.That(model.M41, Is.GreaterThan(0.01f));
        Assert.That(model.M42, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(model.M43, Is.EqualTo(0f).Within(1e-4f));

        Frame(ctx, grab + new Vector2(100f, 0f), false, view, projection, op, GizmoMode.World, ref model);
        Assert.That(ctx.Using, Is.False);
    }

    [Test]
    public void U5_TranslateX_Perspective_World_NegativeDirection()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        Matrix4x4 model = Matrix4x4.Identity;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.TranslateX, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, GizmoOperation.TranslateX, GizmoMode.World, ref model);
        Frame(ctx, grab + new Vector2(-80f, 0f), true, view, projection, GizmoOperation.TranslateX, GizmoMode.World, ref model);
        Assert.That(model.M41, Is.LessThan(-0.01f));
    }

    [Test]
    public void U5_TranslateX_Perspective_Local_RotatedModel()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        GizmoContext ctx = CreateContext();
        // Rotate 90 degrees about Z: local X points along world +Y.
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        Matrix4x4 model = Matrix4x4.CreateFromQuaternion(rotation);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.TranslateX, GizmoMode.Local, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0f, 0.55f * sf, 0f), view, projection);

        Frame(ctx, grab, false, view, projection, GizmoOperation.TranslateX, GizmoMode.Local, ref model);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveX));

        Frame(ctx, grab, true, view, projection, GizmoOperation.TranslateX, GizmoMode.Local, ref model);
        // Drag up on screen: local X (world +Y) increases, world X untouched.
        bool manipulated = Frame(ctx, grab + new Vector2(0f, -60f), true, view, projection, GizmoOperation.TranslateX, GizmoMode.Local, ref model);
        Assert.That(manipulated, Is.True);
        Assert.That(model.M42, Is.GreaterThan(0.01f));
        Assert.That(model.M41, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(model.M43, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void U5_TranslateX_Orthographic_World()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Matrix4x4 model = Matrix4x4.Identity;
        const GizmoOperation op = GizmoOperation.TranslateX | GizmoOperation.TranslateY;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref model);
        // 40 px per world unit: 80 px = 2 units along +X.
        bool manipulated = Frame(ctx, grab + new Vector2(80f, 0f), true, view, projection, op, GizmoMode.World, ref model);
        Assert.That(manipulated, Is.True);
        Assert.That(model.M41, Is.EqualTo(2f).Within(0.05f));
        Assert.That(model.M42, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void U5_TranslateX_Orthographic_Local_RotatedModel()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        Matrix4x4 model = Matrix4x4.CreateFromQuaternion(rotation);

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, GizmoOperation.TranslateX, GizmoMode.Local, ref model);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0f, 0.55f * sf, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, GizmoOperation.TranslateX, GizmoMode.Local, ref model);
        // Drag 40 px up on screen: +1 world unit along local X (world +Y).
        bool manipulated = Frame(ctx, grab + new Vector2(0f, -40f), true, view, projection, GizmoOperation.TranslateX, GizmoMode.Local, ref model);
        Assert.That(manipulated, Is.True);
        Assert.That(model.M42, Is.EqualTo(1f).Within(0.05f));
        Assert.That(model.M41, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void U5_Translate_Transform3DOverload_WritesBack()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform3D transform = Transform3D.Identity;
        const GizmoOperation op = GizmoOperation.TranslateX | GizmoOperation.TranslateY;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.55f * sf, 0f, 0f), view, projection);

        Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref transform);
        bool manipulated = Frame(ctx, grab + new Vector2(40f, 0f), true, view, projection, op, GizmoMode.World, ref transform);
        Assert.That(manipulated, Is.True);
        Assert.That(transform.Position.X, Is.EqualTo(1f).Within(0.05f));
        Assert.That(transform.Position.Y, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(transform.Scale, Is.EqualTo(Vector3.One));
    }

    [Test]
    public void U6_Translate2D_LeavesRotationAndScaleUntouched()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform2D transform = new Transform2D(new Vector2(3f, 2f), new Rotation2D(30f), new Vector2(2f, 1.5f));
        Rotation2D originalRotation = transform.Rotation;
        Vector2 originalScale = transform.Scale;
        const GizmoOperation op = GizmoOperation.TranslateX | GizmoOperation.TranslateY;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(3f + 0.55f * sf, 2f, 0f), view, projection);

        Frame(ctx, grab, false, view, projection, op, GizmoMode.World, ref transform);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveX));

        Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref transform);
        // 40 px = 1 world unit along +X.
        bool manipulated = Frame(ctx, grab + new Vector2(40f, 0f), true, view, projection, op, GizmoMode.World, ref transform);
        Assert.That(manipulated, Is.True);
        Assert.That(transform.Position.X, Is.EqualTo(4f).Within(0.05f));
        Assert.That(transform.Position.Y, Is.EqualTo(2f).Within(1e-4f));
        Assert.That(transform.Rotation.S, Is.EqualTo(originalRotation.S).Within(1e-5f));
        Assert.That(transform.Rotation.C, Is.EqualTo(originalRotation.C).Within(1e-5f));
        AreEqual(originalScale, transform.Scale, 1e-5f);
    }

    [Test]
    public void U6_Translate2D_PlaneHandle_MovesBothAxes()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(Vector2.Zero, new Vector2(20f, 15f));
        GizmoContext ctx = CreateContext(orthographic: true);
        Transform2D transform = new Transform2D(Vector2.Zero, Rotation2D.Identity, Vector2.One);
        const GizmoOperation op = GizmoOperation.TranslateX | GizmoOperation.TranslateY;

        Frame(ctx, new Vector2(5f, 5f), false, view, projection, op, GizmoMode.World, ref transform);
        float sf = ctx.ScreenFactor;
        Vector2 grab = ToScreen(new Vector3(0.65f * sf, 0.65f * sf, 0f), view, projection);

        Frame(ctx, grab, false, view, projection, op, GizmoMode.World, ref transform);
        Assert.That(ctx.FrameHoverType, Is.EqualTo(GizmoMoveType.MoveXY));

        Frame(ctx, grab, true, view, projection, op, GizmoMode.World, ref transform);
        bool manipulated = Frame(ctx, grab + new Vector2(40f, -40f), true, view, projection, op, GizmoMode.World, ref transform);
        Assert.That(manipulated, Is.True);
        Assert.That(transform.Position.X, Is.EqualTo(1f).Within(0.05f));
        Assert.That(transform.Position.Y, Is.EqualTo(1f).Within(0.05f));
    }
}
