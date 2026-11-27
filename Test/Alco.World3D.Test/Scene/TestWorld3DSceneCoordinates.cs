using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>Tests the CPU-side World3D spatial contract without a GPU.</summary>
public sealed class TestWorld3DSceneCoordinates
{
    private static readonly Extent2D s_viewport = new(800, 400);

    /// <summary>Checks floor semantics for negative cells, including residual rounding at a boundary.</summary>
    [Test]
    public void SplitUsesFloorOnBothSidesOfCellBoundaries()
    {
        Assert.That(World3DSceneCoordinates.TrySplitPosition(new(511.75, -512.25, -0.5), out var cell, out var local), Is.True);
        Assert.That(cell, Is.EqualTo(new World3DCell(0, -2, -1)));
        Assert.That(local, Is.EqualTo(new Vector3(511.75f, 511.75f, 511.5f)));

        Assert.That(World3DSceneCoordinates.TrySplitPosition(new(-512, 512, -double.Epsilon), out cell, out local), Is.True);
        Assert.That(cell, Is.EqualTo(new World3DCell(-1, 1, -1)));
        Assert.That(local, Is.EqualTo(new Vector3(0f, 0f, 512f)));
    }

    /// <summary>Checks that the supported cell range permits signed 32-bit subtraction and rejects invalid positions.</summary>
    [Test]
    public void SplitRejectsPositionsOutsideTheSignedSubtractionRange()
    {
        const double minOrigin = -512.0 * (1 << 30);
        const double maxExclusive = 512.0 * (1 << 30);
        Assert.That(World3DSceneCoordinates.TrySplitPosition(new(minOrigin, Math.BitDecrement(maxExclusive), 0), out var cell, out _), Is.True);
        Assert.That(cell.X, Is.EqualTo(-(1 << 30)));
        Assert.That(cell.Y, Is.EqualTo((1 << 30) - 1));
        Assert.That(checked(cell.Y - cell.X), Is.EqualTo(int.MaxValue));
        Assert.That(World3DSceneCoordinates.TrySplitPosition(new(Math.BitDecrement(minOrigin), 0, 0), out _, out _), Is.False);
        Assert.That(World3DSceneCoordinates.TrySplitPosition(new(0, maxExclusive, 0), out _, out _), Is.False);
        Assert.That(World3DSceneCoordinates.TrySplitPosition(new(0, 0, double.NaN), out _, out _), Is.False);
        Assert.That(World3DSceneCoordinates.TrySplitPosition(new(double.PositiveInfinity, 0, 0), out _, out _), Is.False);
    }

    /// <summary>Checks that camera-relative reconstruction retains small offsets across distant positive and negative cell boundaries.</summary>
    [Test]
    public void PivotRetainsSubUnitOffsetsAcrossDistantCells()
    {
        const double edge = 512.0 * 1_000_000;
        var camera = CameraAt(new(edge - 1.0 / 256, -edge + 1.0 / 128, edge + 0.25));
        var instance = InstanceAt(new(edge + 1.0 / 128, -edge - 1.0 / 256, edge + 0.25 + 1.0 / 1024));
        Assert.That(World3DView.TryCreate(camera, s_viewport, null, out var view), Is.True);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance, Vector3.Zero, Vector3.One, out var spatial), Is.True);

        Assert.That(view.EyeCell, Is.EqualTo(new World3DCell(999_999, -1_000_000, 1_000_000)));
        Assert.That(spatial.Cell, Is.EqualTo(new World3DCell(1_000_000, -1_000_001, 1_000_000)));
        Assert.That(spatial.ObjectToCell.Translation, Is.EqualTo(new Vector3(1f / 128, 512f - 1f / 256, 0.25f + 1f / 1024)));
        Assert.That(PivotCameraRelative(spatial, view), Is.EqualTo(new Vector3(3f / 256, -3f / 256, 1f / 1024)));
    }

    /// <summary>Checks per-axis, inclusive hysteresis on both sides of the anchor without affecting instance data or the view transform.</summary>
    [TestCase(-1)]
    [TestCase(1)]
    public void AnchorRebasesAtTwoCellsInEitherDirection(int direction)
    {
        var start = CameraAt(default);
        var before = CameraAt(new(direction * (1024 - 0.25), -0.25, 512.25));
        var atThreshold = before with { Eye = before.Eye with { X = direction * 1024.0 } };
        var instance = InstanceAt(new(direction * 1024.0 + 1.5, -0.25, 512.25));
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance, Vector3.Zero, Vector3.One, out var spatial), Is.True);
        Assert.That(World3DView.TryCreate(start, s_viewport, null, out var initial), Is.True);
        Assert.That(World3DView.TryCreate(before, s_viewport, initial.OriginAnchorCell, out var beforeRebase), Is.True);
        Assert.That(beforeRebase.OriginAnchorCell, Is.EqualTo(initial.OriginAnchorCell));
        Assert.That(beforeRebase.EyeCell, Is.Not.EqualTo(initial.EyeCell));

        Assert.That(World3DView.TryCreate(atThreshold, s_viewport, beforeRebase.OriginAnchorCell, out var rebased), Is.True);
        Assert.That(rebased.OriginAnchorCell, Is.EqualTo(new World3DCell(direction * 2, -1, 1)));
        Assert.That(rebased.OriginAnchorCell, Is.EqualTo(rebased.EyeCell));
        Assert.That(World3DView.TryCreate(before, s_viewport, rebased.OriginAnchorCell, out var jitteredBack), Is.True);
        Assert.That(jitteredBack.OriginAnchorCell, Is.EqualTo(rebased.OriginAnchorCell));
        Assert.That(jitteredBack.CameraVP, Is.EqualTo(beforeRebase.CameraVP));
        Assert.That(jitteredBack.CameraInvVP, Is.EqualTo(beforeRebase.CameraInvVP));
        Assert.That(PivotCameraRelative(spatial, jitteredBack), Is.EqualTo(PivotCameraRelative(spatial, beforeRebase)));
        Assert.That(PivotCameraRelative(spatial, rebased), Is.EqualTo(new Vector3(1.5f, 0f, 0f)));
    }

    /// <summary>Checks exact blittable sizes and GPU field offsets.</summary>
    [Test]
    public void PayloadLayoutMatchesGpuLanes()
    {
        Assert.That(Unsafe.SizeOf<World3DCell>(), Is.EqualTo(16));
        AssertOffset<World3DCell>(nameof(World3DCell.X), 0);
        AssertOffset<World3DCell>(nameof(World3DCell.Y), 4);
        AssertOffset<World3DCell>(nameof(World3DCell.Z), 8);
        AssertOffset<World3DCell>(nameof(World3DCell.W), 12);
        Assert.That(new World3DCell(2, -3, 4).W, Is.Zero);

        Assert.That(Unsafe.SizeOf<World3DInstanceSpatialGpu>(), Is.EqualTo(112));
        AssertOffset<World3DInstanceSpatialGpu>(nameof(World3DInstanceSpatialGpu.ObjectToCell), 0);
        AssertOffset<World3DInstanceSpatialGpu>(nameof(World3DInstanceSpatialGpu.Cell), 64);
        AssertOffset<World3DInstanceSpatialGpu>(nameof(World3DInstanceSpatialGpu.BoundsCenterRadius), 80);
        AssertOffset<World3DInstanceSpatialGpu>(nameof(World3DInstanceSpatialGpu.BoundsExtent), 96);

        Assert.That(Unsafe.SizeOf<World3DView>(), Is.EqualTo(192));
        AssertOffset<World3DView>(nameof(World3DView.CameraVP), 0);
        AssertOffset<World3DView>(nameof(World3DView.CameraInvVP), 64);
        AssertOffset<World3DView>(nameof(World3DView.EyeCell), 128);
        AssertOffset<World3DView>(nameof(World3DView.OriginAnchorCell), 144);
        AssertOffset<World3DView>(nameof(World3DView.EyeLocalNear), 160);
        AssertOffset<World3DView>(nameof(World3DView.ViewportSizeInvSize), 176);
    }

    /// <summary>Checks row-vector projection, orientation, reversed depth, and inverse reconstruction with normalized rotation.</summary>
    [TestCase(float.Epsilon)]
    [TestCase(float.MaxValue)]
    public void CameraProjectsInRotatedLeftHandedFrameWithReversedInfiniteDepth(float rotationMagnitude)
    {
        const float near = 0.25f;
        var camera = new World3DCamera(new(512_000_000.25, -512_000_000.5, 7), new(0f, 0f, rotationMagnitude, rotationMagnitude), MathF.PI / 2f, near);
        Assert.That(World3DView.TryCreate(camera, s_viewport, null, out var view), Is.True);
        Assert.That(view.EyeLocalNear, Is.EqualTo(new Vector4(0.25f, 511.5f, 7f, near)));
        Assert.That(view.ViewportSizeInvSize, Is.EqualTo(new Vector4(800f, 400f, 1f / 800f, 1f / 400f)));

        // A +90-degree turn about Z makes +Y forward, -X right, and keeps +Z up.
        Vector4 eyeRelativePoint = new(-near, near, near * 0.5f, 1f);
        Vector4 clip = Vector4.Transform(eyeRelativePoint, view.CameraVP);
        Assert.That(clip.W, Is.EqualTo(near).Within(1e-6f));
        Assert.That(clip.X / clip.W, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(clip.Y / clip.W, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(clip.Z / clip.W, Is.EqualTo(1f).Within(1e-6f));
        Assert.That(Vector4.Distance(Vector4.Transform(clip, view.CameraInvVP), eyeRelativePoint), Is.LessThan(1e-6f));

        Vector4 farClip = Vector4.Transform(new Vector4(0f, near * 1_000_000, 0f, 1f), view.CameraVP);
        Assert.That(farClip.Z / farClip.W, Is.EqualTo(1e-6f).Within(1e-10f));
        Vector4 atInfinity = Vector4.Transform(new Vector4(0f, 0f, 0f, 1f), view.CameraInvVP);
        Assert.That(Vector4.Distance(atInfinity, new Vector4(0f, 1f, 0f, 0f)), Is.LessThan(1e-6f));
    }

    /// <summary>Checks that packed pivot-relative bounds and sphere enclose every corner of a rotated, mirrored, nonuniformly scaled box.</summary>
    [Test]
    public void PackedBoundsEncloseTransformedCorners()
    {
        Vector3 min = new(-1f, -2f, -0.5f);
        Vector3 max = new(2f, 4f, 3.5f);
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(2f, 1f, -3f)), 0.6f);
        Vector3 scale = new(-2f, 3f, 0.25f);
        var instance = new World3DTransform(new(9000.5, -2200.75, 15.25), rotation * 7f, scale);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance, min, max, out var spatial), Is.True);
        Assert.That(spatial.ObjectToCell.Translation, Is.EqualTo(new Vector3(296.5f, 359.25f, 15.25f)));
        Assert.That(spatial.BoundsExtent.W, Is.Zero);

        Matrix4x4 m = spatial.ObjectToCell;
        Vector3 expectedCenter = Vector3.Transform((min + max) * 0.5f * scale, rotation);
        Vector3 packedCenter = new(spatial.BoundsCenterRadius.X, spatial.BoundsCenterRadius.Y, spatial.BoundsCenterRadius.Z);
        Assert.That(Vector3.Distance(packedCenter, expectedCenter), Is.LessThan(1e-6f));

        for (int corner = 0; corner < 8; corner++)
        {
            double x = (corner & 1) == 0 ? min.X : max.X;
            double y = (corner & 2) == 0 ? min.Y : max.Y;
            double z = (corner & 4) == 0 ? min.Z : max.Z;
            double dx = Math.Abs(x * m.M11 + y * m.M21 + z * m.M31 - packedCenter.X);
            double dy = Math.Abs(x * m.M12 + y * m.M22 + z * m.M32 - packedCenter.Y);
            double dz = Math.Abs(x * m.M13 + y * m.M23 + z * m.M33 - packedCenter.Z);
            Assert.That(dx, Is.LessThanOrEqualTo((double)spatial.BoundsExtent.X));
            Assert.That(dy, Is.LessThanOrEqualTo((double)spatial.BoundsExtent.Y));
            Assert.That(dz, Is.LessThanOrEqualTo((double)spatial.BoundsExtent.Z));
            Assert.That(Math.Sqrt(dx * dx + dy * dy + dz * dz), Is.LessThanOrEqualTo((double)spatial.BoundsCenterRadius.W));
        }
    }

    /// <summary>Checks rejection of unusable projections, transforms, and bounds without exceptions.</summary>
    [Test]
    public void TryMethodsRejectInvalidInputs()
    {
        var camera = CameraAt(default);
        Assert.That(World3DView.TryCreate(camera, new Extent2D(800, 0), null, out _), Is.False);
        Assert.That(World3DView.TryCreate(camera with { Fov = MathF.PI }, s_viewport, null, out _), Is.False);
        Assert.That(World3DView.TryCreate(camera with { Near = float.Epsilon }, s_viewport, null, out _), Is.False);
        Assert.That(World3DView.TryCreate(camera with { Rotation = default }, s_viewport, null, out _), Is.False);
        Assert.That(World3DView.TryCreate(camera, s_viewport, new World3DCell(int.MaxValue, 0, 0), out _), Is.False);

        var instance = InstanceAt(default);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance with { Scale = new(1f, 0f, 1f) }, Vector3.Zero, Vector3.One, out _), Is.False);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance with { Rotation = new(float.NaN, 0f, 0f, 1f) }, Vector3.Zero, Vector3.One, out _), Is.False);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance, Vector3.One, Vector3.Zero, out _), Is.False);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance, new(float.NaN, 0f, 0f), Vector3.One, out _), Is.False);

        Vector3 huge = new(float.MaxValue);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance, -huge, huge, out _), Is.False, "The sphere radius would overflow.");
        Vector3 hugeX = new(float.MaxValue, 0f, 0f);
        Assert.That(World3DSceneCoordinates.TryPackInstance(instance with { Scale = new(2f, 1f, 1f) }, -hugeX, hugeX, out _), Is.False,
            "The X half extent would overflow.");
    }

    private static World3DCamera CameraAt(World3DPosition eye) => new(eye, Quaternion.Identity, 0.9f, 0.125f);

    private static World3DTransform InstanceAt(World3DPosition position) => new(position, Quaternion.Identity, Vector3.One);

    private static Vector3 PivotCameraRelative(in World3DInstanceSpatialGpu spatial, in World3DView view)
    {
        Vector3 cellDelta = new(spatial.Cell.X - view.EyeCell.X, spatial.Cell.Y - view.EyeCell.Y, spatial.Cell.Z - view.EyeCell.Z);
        Vector3 eyeLocal = new(view.EyeLocalNear.X, view.EyeLocalNear.Y, view.EyeLocalNear.Z);
        return cellDelta * World3DSceneCoordinates.CellSize + (spatial.ObjectToCell.Translation - eyeLocal);
    }

    private static void AssertOffset<T>(string field, int offset) where T : struct =>
        Assert.That(Marshal.OffsetOf<T>(field).ToInt32(), Is.EqualTo(offset), field);
}
