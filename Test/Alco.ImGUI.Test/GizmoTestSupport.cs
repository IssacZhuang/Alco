using System.Numerics;

namespace Alco.ImGUI.Test;

/// <summary>
/// Shared helpers for gizmo tests: synthetic left-handed cameras, frame pumping
/// with injected mouse input, and tolerance assertions.
/// </summary>
internal static class GizmoTestSupport
{
    /// <summary>The viewport used by all tests, in pixels.</summary>
    public static readonly Rect Viewport = new Rect(0f, 0f, 800f, 600f);

    /// <summary>Creates a perspective camera looking from <paramref name="eye"/> at <paramref name="target"/>.</summary>
    public static (Matrix4x4 View, Matrix4x4 Projection) CreatePerspectiveCamera(Vector3 eye, Vector3 target, Vector3 up, float fovRadians = 1.0f)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAtLeftHanded(eye, target, up);
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(fovRadians, Viewport.Size.X / Viewport.Size.Y, 0.1f, 100f);
        return (view, projection);
    }

    /// <summary>Creates an orthographic 2D camera looking along +Z, mirroring <c>CameraData2D</c>.</summary>
    public static (Matrix4x4 View, Matrix4x4 Projection) CreateOrthoCamera2D(Vector2 center, Vector2 size)
    {
        Vector3 position = new Vector3(center, 0f);
        Matrix4x4 view = Matrix4x4.CreateLookAtLeftHanded(position, position + Vector3.UnitZ, Vector3.UnitY);
        Vector2 halfSize = size * 0.5f;
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenterLeftHanded(-halfSize.X, halfSize.X, -halfSize.Y, halfSize.Y, -1f, 1f);
        return (view, projection);
    }

    /// <summary>Creates a gizmo context for headless tests.</summary>
    public static GizmoContext CreateContext(bool orthographic = false)
    {
        return new GizmoContext { IsOrthographic = orthographic };
    }

    /// <summary>
    /// Pumps one frame: feeds the input snapshot and runs the core on the model matrix.
    /// Returns the manipulated flag.
    /// </summary>
    public static bool Frame(GizmoContext ctx, Vector2 mouse, bool down,
        in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Matrix4x4 model, GizmoSnap? snap = null)
    {
        ctx.BeginFrame(Viewport, new GizmoInput(mouse, down, Vector2.Zero));
        return GizmoCore.Manipulate(ctx, view, projection, operation, mode, ref model, out _, snap);
    }

    /// <summary>Pumps one frame for a 3D transform.</summary>
    public static bool Frame(GizmoContext ctx, Vector2 mouse, bool down,
        in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Transform3D transform, GizmoSnap? snap = null)
    {
        ctx.BeginFrame(Viewport, new GizmoInput(mouse, down, Vector2.Zero));
        return GizmoCore.Manipulate(ctx, view, projection, operation, mode, ref transform, snap);
    }

    /// <summary>Pumps one frame for a 2D transform.</summary>
    public static bool Frame(GizmoContext ctx, Vector2 mouse, bool down,
        in Matrix4x4 view, in Matrix4x4 projection,
        GizmoOperation operation, GizmoMode mode, ref Transform2D transform, GizmoSnap? snap = null)
    {
        ctx.BeginFrame(Viewport, new GizmoInput(mouse, down, Vector2.Zero));
        return GizmoCore.Manipulate(ctx, view, projection, operation, mode, ref transform, snap);
    }

    /// <summary>Projects a world point to screen pixels through view * projection.</summary>
    public static Vector2 ToScreen(Vector3 world, in Matrix4x4 view, in Matrix4x4 projection)
    {
        return GizmoMath.WorldToScreen(world, view * projection, Viewport);
    }

    /// <summary>Projects a world point to screen pixels through a clip matrix.</summary>
    public static Vector2 ToScreen(Vector3 world, in Matrix4x4 mvp)
    {
        return GizmoMath.WorldToScreen(world, mvp, Viewport);
    }

    public static void AreEqual(Vector2 expected, Vector2 actual, float tolerance)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance), "X mismatch");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance), "Y mismatch");
    }

    public static void AreEqual(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance), "X mismatch");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance), "Y mismatch");
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance), "Z mismatch");
    }

    /// <summary>Asserts two quaternions describe the same rotation (q and -q are equivalent).</summary>
    public static void RotationEquivalent(Quaternion expected, Quaternion actual, float tolerance)
    {
        float dot = Quaternion.Dot(expected, actual);
        Assert.That(MathF.Abs(dot), Is.EqualTo(1f).Within(tolerance), "Quaternion mismatch");
    }
}
