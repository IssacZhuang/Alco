using System.Numerics;
using NUnit.Framework;

using Alco.World3D;

namespace Alco.World3D.Test;

public class TestVoxelGiClipmap
{
    [Test]
    public void InitialUpdateMarksEveryBrickAndRequestsFullReset()
    {
        var clipmap = new VoxelGiClipmap(128, 8, 0.1f, 4);
        var dirty = new List<VoxelGiDirtyBrick>();

        clipmap.UpdateOrigins(Vector3.Zero);

        Assert.That(clipmap.GetPendingBrickCount(0), Is.EqualTo(16 * 16 * 16));
        Assert.That(clipmap.ConsumeFullReset(0), Is.True);
        Assert.That(clipmap.ConsumeFullReset(0), Is.False);
        Assert.That(clipmap.DrainDirtyBricks(0, 32, dirty), Is.EqualTo(32));
        Assert.That(dirty, Has.All.Matches<VoxelGiDirtyBrick>(brick => brick.X < 16 && brick.Y < 16 && brick.Z < 16));
    }

    [Test]
    public void OneBrickScrollRetainsStorageAndMarksOnlyEnteringPlane()
    {
        var clipmap = new VoxelGiClipmap(128, 8, 0.1f, 1);
        var dirty = new List<VoxelGiDirtyBrick>();
        clipmap.UpdateOrigins(Vector3.Zero);
        clipmap.DrainDirtyBricks(0, 16 * 16 * 16, dirty);
        clipmap.ConsumeFullReset(0);

        clipmap.UpdateOrigins(new Vector3(0.81f, 0.0f, 0.0f));

        Assert.That(clipmap.GetRingOffset(0), Is.EqualTo(new Vector4(8.0f, 0.0f, 0.0f, 0.0f)));
        Assert.That(clipmap.GetPendingBrickCount(0), Is.EqualTo(16 * 16));
        clipmap.DrainDirtyBricks(0, 16 * 16, dirty);
        Assert.That(dirty, Has.Count.EqualTo(16 * 16));
        Assert.That(dirty, Has.All.Matches<VoxelGiDirtyBrick>(brick => brick.X == 15));
        Assert.That(clipmap.ConsumeFullReset(0), Is.False);
    }

    [Test]
    public void StructuralInvalidationPreemptsStreamingWork()
    {
        var clipmap = new VoxelGiClipmap(128, 8, 0.1f, 1);
        var dirty = new List<VoxelGiDirtyBrick>();
        clipmap.UpdateOrigins(Vector3.Zero);
        clipmap.DrainDirtyBricks(0, 16 * 16 * 16, dirty);
        clipmap.UpdateOrigins(new Vector3(0.81f, 0.0f, 0.0f));

        Vector4 origin = clipmap.GetOriginAndVoxelSize(0);
        Vector3 editPosition = new(origin.X + 0.4f, origin.Y + 0.4f, origin.Z + 0.4f);
        clipmap.Invalidate(new BoundingBox3D(editPosition, editPosition));
        clipmap.DrainDirtyBricks(0, 1, dirty);

        Assert.That(dirty, Has.Count.EqualTo(1));
        Assert.That(dirty[0].X, Is.EqualTo(0));
        Assert.That(dirty[0].Y, Is.EqualTo(0));
        Assert.That(dirty[0].Z, Is.EqualTo(0));
    }

    [Test]
    public void TeleportRequestsFullResetAndRestartsToroidalOffset()
    {
        var clipmap = new VoxelGiClipmap(128, 8, 0.1f, 1);
        var dirty = new List<VoxelGiDirtyBrick>();
        clipmap.UpdateOrigins(Vector3.Zero);
        clipmap.DrainDirtyBricks(0, 16 * 16 * 16, dirty);
        clipmap.ConsumeFullReset(0);

        clipmap.UpdateOrigins(new Vector3(1000.0f, 0.0f, 0.0f));

        Assert.That(clipmap.ConsumeFullReset(0), Is.True);
        Assert.That(clipmap.GetRingOffset(0), Is.EqualTo(Vector4.Zero));
        Assert.That(clipmap.GetPendingBrickCount(0), Is.EqualTo(16 * 16 * 16));
    }

    [Test]
    public void BoundsTransformIncludesRotationScaleAndTranslation()
    {
        var bounds = new BoundingBox3D(new Vector3(-1.0f, -2.0f, -0.5f), new Vector3(1.0f, 2.0f, 0.5f));
        Matrix4x4 transform = Matrix4x4.CreateScale(2.0f, 3.0f, 4.0f)
            * Matrix4x4.CreateRotationZ(MathF.PI * 0.5f)
            * Matrix4x4.CreateTranslation(10.0f, 20.0f, 30.0f);

        BoundingBox3D transformed = bounds.Transform(transform);

        Assert.That(transformed.Min.X, Is.EqualTo(4.0f).Within(0.0001f));
        Assert.That(transformed.Max.X, Is.EqualTo(16.0f).Within(0.0001f));
        Assert.That(transformed.Min.Y, Is.EqualTo(18.0f).Within(0.0001f));
        Assert.That(transformed.Max.Y, Is.EqualTo(22.0f).Within(0.0001f));
        Assert.That(transformed.Min.Z, Is.EqualTo(28.0f).Within(0.0001f));
        Assert.That(transformed.Max.Z, Is.EqualTo(32.0f).Within(0.0001f));
    }

    [Test]
    public void PagePoolReusesToroidalSlotWithoutAllocatingAnotherPage()
    {
        var pool = new VoxelGiPagePool(2, 1, 128, 8);
        var brick = new VoxelGiDirtyBrick(0, 0, 0);

        Assert.That(pool.TrySetResident(0, brick, Vector4.Zero, true), Is.True);
        Assert.That(pool.AllocatedPageCount, Is.EqualTo(1));
        uint originalEntry = pool.GetPageTable(0)[0];

        var incomingBrick = new VoxelGiDirtyBrick(15, 0, 0);
        Assert.That(pool.TrySetResident(0, incomingBrick, new Vector4(8.0f, 0.0f, 0.0f, 0.0f), true), Is.True);

        Assert.That(pool.AllocatedPageCount, Is.EqualTo(1));
        Assert.That(pool.GetPageTable(0)[0], Is.EqualTo(originalEntry));
    }

    [Test]
    public void PagePoolReportsCapacityAndReleasesPages()
    {
        var pool = new VoxelGiPagePool(1, 1, 128, 8);
        var first = new VoxelGiDirtyBrick(0, 0, 0);
        var second = new VoxelGiDirtyBrick(1, 0, 0);

        Assert.That(pool.TrySetResident(0, first, Vector4.Zero, true), Is.True);
        Assert.That(pool.TrySetResident(0, second, Vector4.Zero, true), Is.False);
        Assert.That(pool.TrySetResident(0, first, Vector4.Zero, false), Is.True);
        Assert.That(pool.TrySetResident(0, second, Vector4.Zero, true), Is.True);
        Assert.That(pool.AllocatedPageCount, Is.EqualTo(1));
    }

    [Test]
    public void ProbeGridOriginMovesOnlyInWholeProbeSteps()
    {
        Vector4 first = VoxelGiProbeGrid.CalculateSnappedOrigin(
            Vector3.Zero,
            0.8f,
            16,
            16,
            8);
        Vector4 withinCell = VoxelGiProbeGrid.CalculateSnappedOrigin(
            new Vector3(0.39f, 0.0f, 0.0f),
            0.8f,
            16,
            16,
            8);
        Vector4 nextCell = VoxelGiProbeGrid.CalculateSnappedOrigin(
            new Vector3(0.41f, 0.0f, 0.0f),
            0.8f,
            16,
            16,
            8);

        Assert.That(withinCell, Is.EqualTo(first));
        Assert.That(nextCell.X - first.X, Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(nextCell.W, Is.EqualTo(0.8f));
    }

    [Test]
    public void FailedBrickWorkCanBeRequeuedAtHighPriority()
    {
        var clipmap = new VoxelGiClipmap(16, 8, 1.0f, 1);
        var dirty = new List<VoxelGiDirtyBrick>();
        clipmap.UpdateOrigins(Vector3.Zero);
        clipmap.DrainDirtyBricks(0, 1, dirty);
        VoxelGiDirtyBrick failedBrick = dirty[0];

        clipmap.RequeueDirtyBrick(0, failedBrick);
        clipmap.DrainDirtyBricks(0, 1, dirty);

        Assert.That(dirty, Has.Count.EqualTo(1));
        Assert.That(dirty[0].X, Is.EqualTo(failedBrick.X));
        Assert.That(dirty[0].Y, Is.EqualTo(failedBrick.Y));
        Assert.That(dirty[0].Z, Is.EqualTo(failedBrick.Z));
    }
}
