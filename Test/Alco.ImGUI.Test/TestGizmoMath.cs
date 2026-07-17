using System.Numerics;
using static Alco.ImGUI.Test.GizmoTestSupport;

namespace Alco.ImGUI.Test;

/// <summary>
/// U1-U3: TRS decompose/recompose round-trip, world-to-screen parity with
/// <see cref="CameraMathUtility"/>, and screen-to-ray parity with it.
/// </summary>
[TestFixture]
public class TestGizmoMath
{
    [Test]
    public void U1_TRS_RoundTrip_UniformScale()
    {
        Vector3 translation = new Vector3(3f, -2f, 7f);
        Quaternion rotation = math.quaternion(10f, 20f, 30f);
        Vector3 scale = new Vector3(1.5f, 1.5f, 1.5f);

        Matrix4x4 matrix = GizmoMath.Recompose(translation, rotation, scale);
        Assert.That(GizmoMath.TryDecompose(matrix, out Vector3 t2, out Quaternion r2, out Vector3 s2), Is.True);

        AreEqual(translation, t2, 1e-4f);
        RotationEquivalent(rotation, r2, 1e-4f);
        AreEqual(scale, s2, 1e-4f);
    }

    [Test]
    public void U1_TRS_RoundTrip_NonUniformScale()
    {
        Vector3 translation = new Vector3(-5f, 0.5f, 12f);
        Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1f, 2f, 3f)), 1.1f));
        Vector3 scale = new Vector3(2f, 3f, 4f);

        Matrix4x4 matrix = GizmoMath.Recompose(translation, rotation, scale);
        Assert.That(GizmoMath.TryDecompose(matrix, out Vector3 t2, out Quaternion r2, out Vector3 s2), Is.True);

        AreEqual(translation, t2, 1e-4f);
        RotationEquivalent(rotation, r2, 1e-4f);
        AreEqual(scale, s2, 1e-4f);

        // Recomposing the decomposed parts reproduces the matrix element-wise.
        Matrix4x4 recomposed = GizmoMath.Recompose(t2, r2, s2);
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                Assert.That(recomposed[row, col], Is.EqualTo(matrix[row, col]).Within(1e-3f), $"M{row}{col} mismatch");
            }
        }
    }

    [Test]
    public void U1_TRS_RoundTrip_Identity()
    {
        Matrix4x4 matrix = Matrix4x4.Identity;
        Assert.That(GizmoMath.TryDecompose(matrix, out Vector3 t2, out Quaternion r2, out Vector3 s2), Is.True);
        AreEqual(Vector3.Zero, t2, 1e-6f);
        RotationEquivalent(Quaternion.Identity, r2, 1e-6f);
        AreEqual(Vector3.One, s2, 1e-6f);
    }

    [Test]
    public void U1_TRS_Decompose_DegenerateScaleFails()
    {
        Matrix4x4 matrix = Matrix4x4.CreateScale(0f, 1f, 1f);
        Assert.That(GizmoMath.TryDecompose(matrix, out _, out _, out _), Is.False);
    }

    [Test]
    public void U2_WorldToScreen_Parity_Perspective()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(new Vector3(0f, 0f, -10f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 viewProjection = view * projection;

        Vector2[] points = { Vector2.Zero, new Vector2(3f, 2f), new Vector2(-5f, 7f), new Vector2(10f, -4f), new Vector2(-8f, -8f) };
        foreach (Vector2 point in points)
        {
            Vector2 expected = CameraMathUtility.WorldPointToScreen2D(point, viewProjection, Viewport.Size);
            Vector2 actual = GizmoMath.WorldToScreen(new Vector3(point, 0f), viewProjection, Viewport);
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(0.5f), $"parity mismatch at {point}");
        }
    }

    [Test]
    public void U2_WorldToScreen_Parity_Orthographic()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(new Vector2(5f, 3f), new Vector2(20f, 15f));
        Matrix4x4 viewProjection = view * projection;

        Vector2[] points = { new Vector2(5f, 3f), new Vector2(0f, 0f), new Vector2(14.9f, 10.4f), new Vector2(-4.9f, -4.4f) };
        foreach (Vector2 point in points)
        {
            Vector2 expected = CameraMathUtility.WorldPointToScreen2D(point, viewProjection, Viewport.Size);
            Vector2 actual = GizmoMath.WorldToScreen(new Vector3(point, 0f), viewProjection, Viewport);
            Assert.That(Vector2.Distance(expected, actual), Is.LessThan(0.5f), $"parity mismatch at {point}");
        }
    }

    [Test]
    public void U3_ScreenToRay_Parity_Perspective()
    {
        Vector3 eye = Vector3.Zero;
        (Matrix4x4 view, Matrix4x4 projection) = CreatePerspectiveCamera(eye, Vector3.UnitZ, Vector3.UnitY);
        Matrix4x4 viewProjection = view * projection;
        Matrix4x4.Invert(viewProjection, out Matrix4x4 viewProjectionInverse);

        Vector2[] pixels = { new Vector2(400f, 300f), new Vector2(100f, 100f), new Vector2(700f, 500f), new Vector2(400f, 0f), new Vector2(0f, 600f) };
        foreach (Vector2 pixel in pixels)
        {
            Ray3D expected = CameraMathUtility.ScreenPointToRayPerspective(pixel, Viewport.Size, viewProjection, eye);
            GizmoMath.ComputeCameraRay(pixel, viewProjectionInverse, false, Viewport, out Vector3 origin, out Vector3 direction);

            float dot = Vector3.Dot(Vector3.Normalize(expected.Displacement), direction);
            Assert.That(dot, Is.EqualTo(1f).Within(1e-3f), $"direction mismatch at {pixel}");

            // The gizmo ray must pass through the camera position.
            float offAxis = Vector3.Cross(origin - eye, direction).Length();
            Assert.That(offAxis, Is.LessThan(1e-3f), $"ray does not pass through eye at {pixel}");
        }
    }

    [Test]
    public void U3_ScreenToRay_Parity_Orthographic()
    {
        (Matrix4x4 view, Matrix4x4 projection) = CreateOrthoCamera2D(new Vector2(5f, 3f), new Vector2(20f, 15f));
        Matrix4x4 viewProjection = view * projection;
        Matrix4x4.Invert(viewProjection, out Matrix4x4 viewProjectionInverse);

        Vector2[] pixels = { new Vector2(400f, 300f), new Vector2(0f, 0f), new Vector2(799f, 599f), new Vector2(250f, 450f) };
        foreach (Vector2 pixel in pixels)
        {
            Ray3D expected = CameraMathUtility.ScreenPointToRayOrthographic(pixel, Viewport.Size, viewProjection, Vector3.UnitZ, -1f);
            GizmoMath.ComputeCameraRay(pixel, viewProjectionInverse, false, Viewport, out Vector3 origin, out Vector3 direction);

            float dot = Vector3.Dot(Vector3.Normalize(expected.Displacement), direction);
            Assert.That(dot, Is.EqualTo(1f).Within(1e-4f), $"direction mismatch at {pixel}");
            AreEqual(expected.Origin, origin, 1e-3f);
        }
    }

    [Test]
    public void U9_SnapMath_StepAndHysteresis()
    {
        // Below half a step snaps down, above half a step snaps up, exactly half stays.
        float value = 0.4f;
        GizmoMath.ComputeSnap(ref value, 1f);
        Assert.That(value, Is.EqualTo(0f).Within(1e-6f));

        value = 0.6f;
        GizmoMath.ComputeSnap(ref value, 1f);
        Assert.That(value, Is.EqualTo(1f).Within(1e-6f));

        value = 0.5f;
        GizmoMath.ComputeSnap(ref value, 1f);
        Assert.That(value, Is.EqualTo(0.5f).Within(1e-6f));

        value = -0.6f;
        GizmoMath.ComputeSnap(ref value, 1f);
        Assert.That(value, Is.EqualTo(-1f).Within(1e-6f));
    }

    [Test]
    public void U9_SnapMath_NonPositiveComponentsSkipped()
    {
        Vector3 value = new Vector3(0.6f, 0.6f, 0.6f);
        GizmoMath.ComputeSnap(ref value, new Vector3(1f, 0f, -1f));
        Assert.That(value.X, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(value.Y, Is.EqualTo(0.6f).Within(1e-6f));
        Assert.That(value.Z, Is.EqualTo(0.6f).Within(1e-6f));
    }
}
