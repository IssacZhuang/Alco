using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>Exercises the persistent World3D scene through headless recording and submission.</summary>
[TestFixture]
public sealed class TestWorld3DScene
{
    private static readonly Extent2D s_viewport = new(64, 64);
    private static readonly VertexPBR[] s_vertices =
    [
        new(Vector3.Zero, Vector3.UnitZ, Vector2.Zero),
        new(Vector3.UnitX, Vector3.UnitZ, Vector2.UnitX),
        new(Vector3.UnitY, Vector3.UnitZ, Vector2.UnitY),
    ];

    private static ReadOnlySpan<uint> TriangleIndices => [2u, 0u, 1u];
    private static ulong TriangleBytes => (ulong)(s_vertices.Length * VertexPBR.SizeInBytes + TriangleIndices.Length * sizeof(uint));

    /// <summary>Checks live edits and rejects foreign or wrong-generation handles without changing the live instance.</summary>
    [Test]
    public void LiveCrudRejectsForeignAndWrongGenerationHandles()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        using World3DScene otherScene = new(harness.System);
        World3DInstanceDesc original = CreateInstanceDescription(scene);
        World3DInstanceDesc foreign = CreateInstanceDescription(otherScene);
        WorldInstanceHandle instance = AddInstance(scene, original);
        WorldInstanceHandle otherInstance = AddInstance(otherScene, foreign);
        Assert.That(otherInstance.Slot, Is.EqualTo(instance.Slot));
        Assert.That(otherInstance.Generation, Is.EqualTo(instance.Generation));

        World3DInstanceDesc[] invalidReferences =
        [
            original with { Sector = foreign.Sector },
            original with { Geometry = foreign.Geometry },
            original with { Material = foreign.Material },
            original with { Sector = original.Sector with { Generation = original.Sector.Generation + 1 } },
            original with { Geometry = original.Geometry with { Generation = original.Geometry.Generation + 1 } },
            original with { Material = original.Material with { Generation = original.Material.Generation + 1 } },
        ];
        foreach (World3DInstanceDesc invalid in invalidReferences)
        {
            Assert.That(scene.TryAddInstance(invalid, out _), Is.False, $"Adding with {invalid}");
            Assert.That(scene.TryUpdateInstance(instance, invalid), Is.False, $"Updating with {invalid}");
        }
        foreach (WorldInstanceHandle invalid in new[] { otherInstance, instance with { Generation = instance.Generation + 1 } })
        {
            Assert.That(scene.TryGetInstance(invalid, out _), Is.False);
            Assert.That(scene.TryUpdateInstance(invalid, original), Is.False);
            Assert.That(scene.TryRemoveInstance(invalid), Is.False);
        }
        Assert.That(scene.InstanceCount, Is.EqualTo(1));
        Assert.That(scene.TryGetInstance(instance, out var current), Is.True);
        Assert.That(current, Is.EqualTo(original));
        Assert.That(scene.TryRemoveGeometry(original.Geometry), Is.False);
        Assert.That(scene.TryRemoveMaterial(original.Material), Is.False);

        World3DInstanceDesc replacement = CreateInstanceDescription(scene) with { Transform = TransformAt(new(3, 4, 5)), Fade = 0.5f };
        Assert.That(scene.TryUpdateInstance(instance, replacement), Is.True);
        Assert.That(scene.TryUpdateMaterial(replacement.Material, new World3DMaterialDesc { Roughness = 0.25f }), Is.True);
        Assert.That(scene.TryDetachSector(original.Sector), Is.True);
        Assert.That(scene.TryRemoveGeometry(original.Geometry), Is.True);
        Assert.That(scene.TryRemoveMaterial(original.Material), Is.True);
        Assert.That(scene.TryGetInstance(instance, out current), Is.True);
        Assert.That(current, Is.EqualTo(replacement));
        Assert.That(scene.TryRemoveInstance(instance), Is.True);
        Assert.That(scene.TryGetInstance(instance, out _), Is.False);
        Assert.That(scene.TryRemoveInstance(instance), Is.False);
        Assert.That(scene.InstanceCount, Is.Zero);
    }

    /// <summary>Checks that detaching a sector only removes its own instances, not shared resources.</summary>
    [Test]
    public void SectorDetachPreservesSharedResourcesInTheOtherSector()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc first = CreateInstanceDescription(scene);
        World3DInstanceDesc second = first with { Sector = AttachSector(scene), Transform = TransformAt(new(6, 7, 8)) };
        WorldInstanceHandle removed = AddInstance(scene, first);
        WorldInstanceHandle survivor = AddInstance(scene, second);
        QueueTriangle(scene, first.Geometry);
        harness.SubmitFrame();

        Assert.That(scene.TryDetachSector(first.Sector), Is.True);
        Assert.That(scene.TryDetachSector(first.Sector), Is.False);
        Assert.That(scene.TryGetInstance(removed, out _), Is.False);
        Assert.That(scene.TryAddInstance(first, out _), Is.False);
        Assert.That(scene.TryGetInstance(survivor, out var current), Is.True);
        Assert.That(current, Is.EqualTo(second));
        Assert.That(scene.InstanceCount, Is.EqualTo(1));
        Assert.That(scene.IsGeometryLodResident(second.Geometry, 0), Is.True);
        Assert.That(scene.TryRemoveGeometry(second.Geometry), Is.False);
        Assert.That(scene.TryRemoveMaterial(second.Material), Is.False);
        harness.SubmitFrame();
        Assert.That(scene.LastRecordedSync, Is.EqualTo(new World3DSceneSyncStats(0, 0, 0, 1, 0)));

        Assert.That(scene.TryDetachSector(second.Sector), Is.True);
        Assert.That(scene.InstanceCount, Is.Zero);
        Assert.That(scene.TryRemoveGeometry(second.Geometry), Is.True);
        Assert.That(scene.TryRemoveMaterial(second.Material), Is.True);
    }

    /// <summary>Checks that removed GPU slots are not reused before the tombstone submission retires.</summary>
    [Test]
    public void TombstoneSubmissionReclaimsSlotsWithNewGenerations()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc removed = CreateInstanceDescription(scene);
        WorldInstanceHandle removedInstance = AddInstance(scene, removed);
        QueueTriangle(scene, removed.Geometry);
        harness.SubmitFrame();
        Assert.That(scene.IsGeometryLodResident(removed.Geometry, 0), Is.True);

        Assert.That(scene.TryRemoveInstance(removedInstance), Is.True);
        Assert.That(scene.TryRemoveGeometry(removed.Geometry), Is.True);
        Assert.That(scene.TryRemoveMaterial(removed.Material), Is.True);
        Assert.That(scene.TryGetInstance(removedInstance, out _), Is.False);
        Assert.That(scene.IsGeometryLodResident(removed.Geometry, 0), Is.False);

        AssertSlotsStillRetired();
        harness.RecordFrame();
        harness.Context.DiscardFrame();
        AssertSlotsStillRetired();

        harness.SubmitFrame();
        World3DInstanceDesc reused = CreateInstanceDescription(scene);
        WorldInstanceHandle reusedInstance = AddInstance(scene, reused);
        Assert.That(reused.Geometry, Is.EqualTo(removed.Geometry with { Generation = removed.Geometry.Generation + 1 }));
        Assert.That(reused.Material, Is.EqualTo(removed.Material with { Generation = removed.Material.Generation + 1 }));
        Assert.That(reusedInstance, Is.EqualTo(removedInstance with { Generation = removedInstance.Generation + 1 }));
        Assert.That(scene.TryGetInstance(removedInstance, out _), Is.False);
        Assert.That(scene.TryQueueGeometryLod(removed.Geometry, 0, s_vertices, TriangleIndices), Is.False);
        Assert.That(scene.TryUpdateMaterial(removed.Material, new World3DMaterialDesc()), Is.False);
        Assert.That(scene.TryGetInstance(reusedInstance, out var current), Is.True);
        Assert.That(current, Is.EqualTo(reused));

        void AssertSlotsStillRetired()
        {
            World3DInstanceDesc fresh = CreateInstanceDescription(scene);
            WorldInstanceHandle freshInstance = AddInstance(scene, fresh);
            Assert.That(fresh.Geometry.Slot, Is.Not.EqualTo(removed.Geometry.Slot));
            Assert.That(fresh.Material.Slot, Is.Not.EqualTo(removed.Material.Slot));
            Assert.That(freshInstance.Slot, Is.Not.EqualTo(removedInstance.Slot));
        }
    }

    /// <summary>Checks that a discarded frame publishes nothing and retries the uploads and page initialization.</summary>
    [Test]
    public void DiscardedRecordRetriesGeometryAndTablePatches()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc description = CreateInstanceDescription(scene);
        AddInstance(scene, description);
        QueueTriangle(scene, description.Geometry);

        harness.RecordFrame();
        World3DSceneSyncStats discarded = scene.LastRecordedSync;
        Assert.That(discarded.GeometryBytes, Is.EqualTo(TriangleBytes));
        Assert.That(discarded.GeometryPatches, Is.EqualTo(1));
        Assert.That(discarded.MaterialPatches, Is.EqualTo(1));
        Assert.That(discarded.InstancePatches, Is.EqualTo(1));
        Assert.That(discarded.InitializedTablePages, Is.GreaterThan(0));
        Assert.That(scene.IsGeometryLodResident(description.Geometry, 0), Is.False);
        harness.Context.DiscardFrame();
        Assert.That(scene.IsGeometryLodResident(description.Geometry, 0), Is.False);

        harness.SubmitFrame();
        Assert.That(scene.LastRecordedSync, Is.EqualTo(discarded));
        Assert.That(scene.IsGeometryLodResident(description.Geometry, 0), Is.True);
        harness.SubmitFrame();
        Assert.That(scene.LastRecordedSync, Is.EqualTo(default(World3DSceneSyncStats)));
    }

    /// <summary>Checks aligned budget chunks and keeps the old payload resident until a replacement is submitted in full.</summary>
    [Test]
    public void GeometryBudgetChunksUploadsAndKeepsPreviousLodResident()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        WorldGeometryHandle geometry = RegisterGeometry(scene);
        QueueTriangle(scene, geometry);

        scene.GeometryUploadBudgetBytes = 0;
        harness.SubmitFrame();
        AssertGeometrySync(scene, 0, 0);
        Assert.That(scene.IsGeometryLodResident(geometry, 0), Is.False);

        const ulong chunkBytes = 64;
        scene.GeometryUploadBudgetBytes = chunkBytes + 1;
        ulong remaining = TriangleBytes;
        while (remaining > chunkBytes)
        {
            harness.SubmitFrame();
            AssertGeometrySync(scene, chunkBytes, 0);
            Assert.That(scene.IsGeometryLodResident(geometry, 0), Is.False);
            remaining -= chunkBytes;
        }
        harness.SubmitFrame();
        AssertGeometrySync(scene, remaining, 1);
        Assert.That(scene.IsGeometryLodResident(geometry, 0), Is.True);
        BufferSlice previous = scene.GetGeometryPayloadGpuSlice(geometry, 0);
        Assert.That(previous.SizeBytes, Is.EqualTo(TriangleBytes));

        ReadOnlySpan<uint> replacementIndices = [0u, 1u, 2u];
        ModelGeometry replacement = ModelGeometry.Create<VertexPBR>(harness.System, s_vertices, replacementIndices, Vector3.Zero, Vector3.One);
        try
        {
            Assert.That(scene.TryQueueGeometryLod(geometry, 0, replacement, minimumDistance: 16), Is.True);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        scene.GeometryUploadBudgetBytes = 0;
        harness.SubmitFrame();
        AssertGeometrySync(scene, 0, 0);
        Assert.That(scene.GetGeometryPayloadGpuSlice(geometry, 0), Is.EqualTo(previous));

        // The first copy spans the vertex buffer and part of the separate index buffer.
        ulong replacementChunkBytes = replacement.VertexBuffer.SizeBytes + sizeof(uint);
        scene.GeometryUploadBudgetBytes = replacementChunkBytes;
        harness.SubmitFrame();
        AssertGeometrySync(scene, replacementChunkBytes, 0);
        Assert.That(scene.IsGeometryLodResident(geometry, 0), Is.True);
        Assert.That(scene.GetGeometryPayloadGpuSlice(geometry, 0), Is.EqualTo(previous));
        Assert.That(replacement.IsDisposed, Is.False);

        scene.GeometryUploadBudgetBytes = TriangleBytes;
        harness.RecordFrame();
        AssertGeometrySync(scene, TriangleBytes - replacementChunkBytes, 1);
        Assert.That(scene.GetGeometryPayloadGpuSlice(geometry, 0), Is.EqualTo(previous));
        Assert.That(replacement.IsDisposed, Is.False);
        harness.Context.SubmitWithCompletion().Wait();
        Assert.That(scene.IsGeometryLodResident(geometry, 0), Is.True);
        Assert.That(scene.GetGeometryPayloadGpuSlice(geometry, 0), Is.Not.EqualTo(previous));
        Assert.That(replacement.IsDisposed, Is.True);
    }

    /// <summary>Checks that repeated edits of live table rows are recorded once per dirty slot.</summary>
    [Test]
    public void RepeatedUpdatesDeduplicateMaterialAndInstancePatches()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc description = CreateInstanceDescription(scene);
        WorldInstanceHandle instance = AddInstance(scene, description);
        AddInstance(scene, description);
        harness.SubmitFrame();

        for (int i = 1; i <= 3; ++i)
        {
            description = description with { Transform = TransformAt(new(i, 2, 3)) };
            Assert.That(scene.TryUpdateInstance(instance, description), Is.True);
            Assert.That(scene.TryUpdateMaterial(description.Material, new World3DMaterialDesc { Metallic = i / 4f }), Is.True);
        }
        harness.SubmitFrame();
        Assert.That(scene.LastRecordedSync, Is.EqualTo(new World3DSceneSyncStats(0, 0, 1, 1, 0)));
        Assert.That(scene.TryGetInstance(instance, out var current), Is.True);
        Assert.That(current, Is.EqualTo(description));
        harness.SubmitFrame();
        Assert.That(scene.LastRecordedSync, Is.EqualTo(default(World3DSceneSyncStats)));
    }

    /// <summary>Checks that crossing the 256-slot page boundary leaves the first page in place.</summary>
    [Test]
    public void Instance257InitializesOnlyOneAdditionalTablePage()
    {
        const int pageSize = 256;
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc description = CreateInstanceDescription(scene);
        WorldInstanceHandle first = AddInstance(scene, description);
        for (int i = 1; i < pageSize; ++i)
            AddInstance(scene, description);
        harness.SubmitFrame();
        BufferSlice firstRow = scene.GetInstanceGpuSlice(first.Slot);
        Assert.That(firstRow.IsEmpty, Is.False);

        WorldInstanceHandle added = AddInstance(scene, description);
        Assert.That(added.Slot, Is.EqualTo(pageSize));
        harness.SubmitFrame();
        Assert.That(scene.LastRecordedSync, Is.EqualTo(new World3DSceneSyncStats(0, 0, 0, 1, 1)));
        Assert.That(scene.GetInstanceGpuSlice(first.Slot), Is.EqualTo(firstRow));
        Assert.That(scene.GetInstanceGpuSlice(added.Slot).IsEmpty, Is.False);
    }

    /// <summary>Checks that rebasing the camera anchor does not dirty persistent scene data.</summary>
    [Test]
    public void OriginAnchorRebaseRecordsNoSceneUploads()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc description = CreateInstanceDescription(scene);
        WorldInstanceHandle instance = AddInstance(scene, description);
        QueueTriangle(scene, description.Geometry);
        World3DView initial = harness.SubmitFrame();
        Assert.That(initial.OriginAnchorCell, Is.EqualTo(new World3DCell(0, 0, 0)));

        World3DView rebased = harness.SubmitFrame(CameraAt(new(2 * World3DSceneCoordinates.CellSize, 0, 0)));
        Assert.That(rebased.OriginAnchorCell, Is.EqualTo(new World3DCell(2, 0, 0)));
        Assert.That(scene.LastRecordedSync, Is.EqualTo(default(World3DSceneSyncStats)));
        Assert.That(scene.IsGeometryLodResident(description.Geometry, 0), Is.True);
        Assert.That(scene.TryGetInstance(instance, out var current), Is.True);
        Assert.That(current, Is.EqualTo(description));
    }

    /// <summary>Checks that headless sync passes stay live and only a supplied output enables the clear pass.</summary>
    [Test]
    public void GraphKeepsHeadlessSyncAndOnlyClearsWhenOutputIsSupplied()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc description = CreateInstanceDescription(scene);
        WorldInstanceHandle instance = AddInstance(scene, description);
        harness.SubmitFrame();

        QueueTriangle(scene, description.Geometry);
        Assert.That(scene.TryUpdateInstance(instance, description with { Fade = 0.5f }), Is.True);
        harness.SubmitFrame();
        AssertGraphPasses(scene, 2);

        using Texture output = harness.System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(17, 9, 1),
            Usage = TextureUsage.ColorAttachment,
        });
        World3DView view = harness.SubmitFrame(output: RenderGraphTarget.FromTexture(output));
        Assert.That(view.ViewportSizeInvSize, Is.EqualTo(new Vector4(17, 9, 1f / 17, 1f / 9)));
        Assert.That(scene.LastRecordedSync, Is.EqualTo(default(World3DSceneSyncStats)));
        AssertGraphPasses(scene, 1);

        harness.SubmitFrame();
        AssertGraphPasses(scene, 0);
    }

    /// <summary>Checks that old and removed material bindings hold their texture leases until submission retirement.</summary>
    [Test]
    public void MaterialPatchAndTombstoneKeepTextureLeasesUntilRetirement()
    {
        using SceneHarness harness = CreateNoGpuScene();
        World3DScene scene = harness.Scene;
        TextureDesc texture = new();
        Assert.That(scene.TryCreateTexture(texture, [255, 0, 0, 255], out var oldTexture), Is.True);
        Assert.That(scene.TryCreateTexture(texture, [0, 255, 0, 255], out var newTexture), Is.True);
        World3DMaterialDesc description = new() { BaseColorTexture = oldTexture };
        WorldMaterialHandle material = RegisterMaterial(scene, description);
        harness.SubmitFrame();
        Assert.That(scene.TryRemoveTexture(oldTexture), Is.False);

        Assert.That(scene.TryUpdateMaterial(material, description with { BaseColorTexture = newTexture }), Is.True);
        Assert.That(scene.TryRemoveTexture(oldTexture), Is.False);
        harness.RecordFrame();
        harness.Context.DiscardFrame();
        Assert.That(scene.TryRemoveTexture(oldTexture), Is.False);
        Assert.That(scene.TryRemoveTexture(newTexture), Is.False);

        harness.SubmitFrame();
        Assert.That(scene.TryRemoveTexture(oldTexture), Is.True);
        Assert.That(scene.TryRemoveTexture(newTexture), Is.False);
        Assert.That(scene.TryRemoveMaterial(material), Is.True);
        Assert.That(scene.TryRemoveTexture(newTexture), Is.False);
        harness.SubmitFrame();
        Assert.That(scene.TryRemoveTexture(newTexture), Is.True);
    }

    /// <summary>Reads geometry and adjacent instance rows on VulkanCompact before and after an in-place instance patch.</summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactUploadsGeometryAndPatchesOnlyTheUpdatedRow()
    {
        using SceneHarness harness = new(CreateVulkanCompactDevice());
        World3DScene scene = harness.Scene;
        Assert.That(harness.System.ActiveBackend, Is.EqualTo(GraphicsBackend.VulkanCompact));
        World3DInstanceDesc first = CreateInstanceDescription(scene) with { Transform = TransformAt(new(3.5, 4.75, 8.5)) };
        World3DInstanceDesc second = first with { Transform = TransformAt(new(9.25, 13.5, 2)) };
        WorldInstanceHandle unchanged = AddInstance(scene, first);
        WorldInstanceHandle updated = AddInstance(scene, second);
        const int lod = 1;
        const float minimumDistance = 24;
        QueueTriangle(scene, first.Geometry, lod, minimumDistance);
        harness.SubmitFrame();

        BufferSlice payload = scene.GetGeometryPayloadGpuSlice(first.Geometry, lod);
        BufferSlice lodRow = scene.GetGeometryLodGpuSlice(first.Geometry.Slot, lod);
        BufferSlice firstRow = scene.GetInstanceGpuSlice(unchanged.Slot);
        BufferSlice secondRow = scene.GetInstanceGpuSlice(updated.Slot);
        Assert.That(firstRow.IsEmpty, Is.False);
        Assert.That(secondRow.Buffer, Is.SameAs(firstRow.Buffer));
        Assert.That(secondRow.ByteOffset, Is.EqualTo(firstRow.ByteOffset + firstRow.SizeBytes));
        BufferSlice rows = firstRow.Buffer!.Slice(firstRow.ByteOffset, firstRow.SizeBytes + secondRow.SizeBytes);
        int rowBytes = checked((int)firstRow.SizeBytes);
        int rowsBytes = checked((int)rows.SizeBytes);
        harness.Context.BeginFrame();
        byte[] before = ReadBackAndSubmit(harness, [rows]);
        Assert.That(MemoryMarshal.Read<World3DInstanceSpatialGpu>(before).ObjectToCell.Translation, Is.EqualTo(new Vector3(3.5f, 4.75f, 8.5f)));
        Assert.That(MemoryMarshal.Read<World3DInstanceSpatialGpu>(before.AsSpan(rowBytes)).ObjectToCell.Translation, Is.EqualTo(new Vector3(9.25f, 13.5f, 2)));

        Vector3 scale = new(2, 3, 0.5f);
        World3DTransform moved = new(new(World3DSceneCoordinates.CellSize + 7.25, -0.5, 2.5), Quaternion.Identity, scale);
        Assert.That(scene.TryUpdateInstance(updated, second with { Transform = moved }), Is.True);
        using Texture output = harness.System.CreateTexture(new TextureDesc
        {
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });
        harness.RecordFrame(output: RenderGraphTarget.FromTexture(output));
        Assert.That(scene.LastRecordedSync, Is.EqualTo(new World3DSceneSyncStats(0, 0, 0, 1, 0)));
        Assert.That(scene.GetInstanceGpuSlice(unchanged.Slot), Is.EqualTo(firstRow));
        Assert.That(scene.GetInstanceGpuSlice(updated.Slot), Is.EqualTo(secondRow));
        byte[] after = ReadBackAndSubmit(harness, [rows, lodRow, payload], output);

        Assert.That(after.AsSpan(0, rowBytes).ToArray(), Is.EqualTo(before.AsSpan(0, rowBytes).ToArray()), "Untouched instance row");
        World3DInstanceSpatialGpu actual = MemoryMarshal.Read<World3DInstanceSpatialGpu>(after.AsSpan(rowBytes));
        Matrix4x4 expected = Matrix4x4.CreateScale(scale);
        expected.Translation = new Vector3(7.25f, World3DSceneCoordinates.CellSize - 0.5f, 2.5f);
        Assert.That(actual.Cell, Is.EqualTo(new World3DCell(1, -1, 0)));
        Assert.That(actual.ObjectToCell, Is.EqualTo(expected));

        int vertexBytes = s_vertices.Length * VertexPBR.SizeInBytes;
        int lodRowBytes = checked((int)lodRow.SizeBytes);
        ReadOnlySpan<byte> lodData = after.AsSpan(rowsBytes, lodRowBytes);
        // Skip the allocator-owned page ID and check the offsets and counts used to find the vertices and indices.
        Assert.That(MemoryMarshal.Cast<byte, uint>(lodData).Slice(1, 4).ToArray(), Is.EqualTo(new uint[]
        {
            (uint)payload.ByteOffset, (uint)(payload.ByteOffset + (ulong)vertexBytes), (uint)s_vertices.Length, (uint)TriangleIndices.Length,
        }));
        Assert.That(MemoryMarshal.Read<float>(lodData[(5 * sizeof(uint))..]), Is.EqualTo(minimumDistance));
        int payloadStart = rowsBytes + lodRowBytes;
        int payloadBytes = checked((int)payload.SizeBytes);
        ReadOnlySpan<byte> payloadData = after.AsSpan(payloadStart, payloadBytes);
        Assert.That(payloadData[..vertexBytes].ToArray(), Is.EqualTo(MemoryMarshal.AsBytes(s_vertices.AsSpan()).ToArray()), "Vertex payload");
        Assert.That(payloadData[vertexBytes..].ToArray(), Is.EqualTo(MemoryMarshal.AsBytes(TriangleIndices).ToArray()), "Index payload");
        Assert.That(after.AsSpan(payloadStart + payloadBytes).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 255 }), "Offscreen clear");
    }

    private static SceneHarness CreateNoGpuScene() => new(Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU, Headless = true }));

    private static GPUDevice CreateVulkanCompactDevice()
    {
        try
        {
            return Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.VulkanCompact, Headless = true });
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"VulkanCompact is unsupported on this host: {ex.Message}");
            throw;
        }
    }

    private static WorldSectorHandle AttachSector(World3DScene scene)
    {
        Assert.That(scene.TryAttachSector(out var sector), Is.True);
        return sector;
    }

    private static WorldGeometryHandle RegisterGeometry(World3DScene scene)
    {
        Assert.That(scene.TryRegisterGeometry(Vector3.Zero, Vector3.One, out var geometry), Is.True);
        return geometry;
    }

    private static WorldMaterialHandle RegisterMaterial(World3DScene scene, World3DMaterialDesc description)
    {
        Assert.That(scene.TryRegisterMaterial(description, out var material), Is.True);
        return material;
    }

    private static World3DInstanceDesc CreateInstanceDescription(World3DScene scene) =>
        new(AttachSector(scene), RegisterGeometry(scene), RegisterMaterial(scene, new World3DMaterialDesc()), TransformAt(default));

    private static WorldInstanceHandle AddInstance(World3DScene scene, World3DInstanceDesc description)
    {
        Assert.That(scene.TryAddInstance(description, out var instance), Is.True);
        return instance;
    }

    private static void QueueTriangle(World3DScene scene, WorldGeometryHandle geometry, int lod = 0, float minimumDistance = 0) =>
        Assert.That(scene.TryQueueGeometryLod(geometry, lod, s_vertices, TriangleIndices, minimumDistance), Is.True);

    private static void AssertGeometrySync(World3DScene scene, ulong bytes, int patches)
    {
        Assert.That(scene.LastRecordedSync.GeometryBytes, Is.EqualTo(bytes));
        Assert.That(scene.LastRecordedSync.GeometryPatches, Is.EqualTo(patches));
    }

    private static void AssertGraphPasses(World3DScene scene, int count)
    {
        Assert.That(scene.LastGraphStatistics.TotalPassCount, Is.EqualTo(count));
        Assert.That(scene.LastGraphStatistics.AlivePassCount, Is.EqualTo(count));
    }

    private static World3DCamera CameraAt(World3DPosition eye) => new(eye, Quaternion.Identity, 0.9f, 0.125f);

    private static World3DTransform TransformAt(World3DPosition pivot) => new(pivot, Quaternion.Identity, Vector3.One);

    private static byte[] ReadBackAndSubmit(SceneHarness harness, ReadOnlySpan<BufferSlice> slices, Texture? rgbaPixel = null)
    {
        const ulong rgbaBytes = 4;
        ulong bytes = rgbaPixel == null ? 0 : rgbaBytes;
        foreach (BufferSlice slice in slices)
        {
            Assert.That(slice.IsEmpty, Is.False);
            bytes += slice.SizeBytes;
        }

        RenderContext context = harness.Context;
        GPUDevice device = harness.System.GetDevice();
        GPUHeap readback = device.CreateGPUHeap(bytes, MemoryType.Readback);
        try
        {
            context.Barrier(Stage.AllCommands, Access.ShaderWrite | Access.TransferWrite | Access.ColorWrite, Stage.Transfer, Access.TransferRead);
            ulong offset = 0;
            foreach (BufferSlice slice in slices)
            {
                context.CopyMemory(slice.GetRange(), readback.Range.GPURange.Slice(offset, slice.SizeBytes));
                offset += slice.SizeBytes;
            }
            if (rgbaPixel != null)
                context.CopyTextureToMemory(rgbaPixel.GetNativeTexture(), readback.Range.GPURange.Slice(offset, rgbaBytes));
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.SubmitWithCompletion().Wait();
            return readback.Range.CpuSpan[..checked((int)bytes)].ToArray();
        }
        catch
        {
            context.DiscardFrame();
            device.WaitIdle();
            throw;
        }
        finally
        {
            readback.Destroy();
        }
    }

    private sealed class SceneHarness : IDisposable
    {
        private readonly GPUDevice _device;
        public readonly RenderingSystem System;
        public readonly RenderContext Context;
        public readonly World3DScene Scene;

        public SceneHarness(GPUDevice device)
        {
            _device = device;
            RenderingSystem? system = null;
            RenderContext? context = null;
            try
            {
                System = system = new RenderingSystem(device, textureHeapByteCount: 2 * 1024 * 1024,
                    bufferHeapByteCount: 2 * 1024 * 1024, uploadStagingByteCount: 64 * 1024);
                Context = context = new RenderContext(System);
                Scene = new World3DScene(System);
            }
            catch
            {
                context?.Dispose();
                system?.Dispose();
                device.Destroy();
                throw;
            }
        }

        /// <summary>Records Phase 0 synchronization and an optional output clear.</summary>
        public World3DView RecordFrame(World3DCamera? camera = null, RenderGraphTarget output = default)
        {
            Context.BeginFrame();
            Assert.That(Scene.TryRecordFrame(Context, camera ?? CameraAt(default), s_viewport, out var view, output, renderWorld: false), Is.True);
            return view;
        }

        public World3DView SubmitFrame(World3DCamera? camera = null, RenderGraphTarget output = default)
        {
            World3DView view = RecordFrame(camera, output);
            Context.SubmitWithCompletion().Wait();
            return view;
        }

        public void Dispose()
        {
            Context.DiscardFrame();
            Scene.Dispose();
            Context.Dispose();
            System.Dispose();
            _device.Destroy();
        }
    }
}
