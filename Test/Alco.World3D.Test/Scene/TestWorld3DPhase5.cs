using System.Numerics;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.IO;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>
/// Phase 5 acceptance tests: external texture ownership, <see cref="World3DSceneAssets"/> end-to-end asset loading,
/// custom disk <c>ISurface</c> composition, LOD switching, material hot-reload, distant sector VoxelGI isolation,
/// and multi-kilometer streaming churn zero-leak verification.
/// </summary>
[TestFixture]
public sealed class TestWorld3DPhase5
{
    private const int FrameSize = 96;
    private static readonly World3DCamera s_originCamera = new(default, Quaternion.Identity, MathF.PI / 2, 0.125f);

    /// <summary>Verifies external texture registration rules, material leases, and non-disposal without GPU hardware.</summary>
    [Test]
    [Category("NoGPU")]
    public void NoGpuExternalTextureOwnershipAndLeaseSafety()
    {
        using Phase5Harness harness = new(GraphicsBackend.NoGPU, assetsRoot: null);
        using Phase5Harness foreignHarness = new(GraphicsBackend.NoGPU, assetsRoot: null);

        World3DScene scene = harness.Scene;
        using Texture external = harness.System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1, 1, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        using Texture foreign = foreignHarness.System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1, 1, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        using Texture depthTex = harness.System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(4, 4, 1),
            Format = Format.D32Float,
            Usage = TextureUsage.DepthStencilAttachment | TextureUsage.Sampled,
        });
        Texture disposedTex = harness.System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1, 1, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        disposedTex.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(harness.System.OwnsTexture(external), Is.True);
            Assert.That(harness.System.OwnsTexture(foreign), Is.False);
            Assert.That(harness.System.OwnsTexture(disposedTex), Is.False);
            Assert.That(scene.TryRegisterExternalTexture(foreign, out _), Is.False);
            Assert.That(scene.TryRegisterExternalTexture(depthTex, out _), Is.False);
            Assert.That(scene.TryRegisterExternalTexture(disposedTex, out _), Is.False);
        });

        Assert.That(scene.TryRegisterExternalTexture(external, out WorldTextureHandle extHandle), Is.True);
        Assert.That(scene.TryCreateTexture(new TextureDesc(), [255, 255, 255, 255], out WorldTextureHandle ownedHandle), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColorTexture = extHandle }, out WorldMaterialHandle matHandle), Is.True);

        World3DSceneResourceStats midStats = scene.GetResourceStats();
        Assert.Multiple(() =>
        {
            Assert.That(midStats.LiveTextures, Is.EqualTo(2));
            Assert.That(midStats.OwnedTextures, Is.EqualTo(1));
            Assert.That(midStats.ExternalTextures, Is.EqualTo(1));
            Assert.That(scene.TryRemoveTexture(extHandle), Is.False, "Leased external texture cannot be removed.");
        });

        Assert.That(scene.TryRemoveMaterial(matHandle), Is.True);
        // Submit a frame so the retired material releases its texture reference.
        harness.SubmitFrame();
        Assert.That(scene.TryRemoveTexture(extHandle), Is.True);
        Assert.That(external.IsDisposed, Is.False, "World3DScene must never dispose caller-owned external textures.");

        // Re-register to verify scene disposal leaves external textures untouched.
        Assert.That(scene.TryRegisterExternalTexture(external, out _), Is.True);
        harness.Scene.Dispose();
        Assert.That(external.IsDisposed, Is.False, "World3DScene.Dispose must not dispose external textures.");
        _ = ownedHandle;
    }

    /// <summary>
    /// Verifies end-to-end <c>.amsh</c>/<c>.amat</c>/<c>.amdl</c> loading via <see cref="World3DSceneAssets"/>,
    /// custom disk <c>ParameterizedSurface</c> rendering, material hot-reload, and GPU LOD switching.
    /// </summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactSceneAssets_LoadsRendersCustomSurfaceHotReloadsAndSwitchesLods()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "alco_w3d_p5_assets_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteSolidPng(Path.Combine(tempDir, "red.png"), 255, 0, 0, 255);
            WriteMultiLodPanelMesh(Path.Combine(tempDir, "panel.amsh"));
            File.WriteAllText(Path.Combine(tempDir, "textured.amat"), """
                {
                    "$type": "Alco.World3D.PbrMaterialAsset",
                    "version": "1.0",
                    "baseColorFactor": { "r": 1, "g": 1, "b": 1, "a": 1 },
                    "textures": { "baseColor": "red.png" }
                }
                """);
            File.WriteAllText(Path.Combine(tempDir, "custom.amat"), """
                {
                    "$type": "Alco.World3D.PbrMaterialAsset",
                    "version": "1.0",
                    "surface": "ParameterizedSurface",
                    "baseColorFactor": { "r": 1, "g": 1, "b": 1, "a": 1 }
                }
                """);
            File.WriteAllText(Path.Combine(tempDir, "textured.amdl"), """
                {
                    "version": "1.0",
                    "mesh": "panel.amsh",
                    "slots": [ { "name": "panel", "material": "textured.amat" } ]
                }
                """);
            File.WriteAllText(Path.Combine(tempDir, "custom.amdl"), """
                {
                    "version": "1.0",
                    "mesh": "panel.amsh",
                    "slots": [ { "name": "panel", "material": "custom.amat" } ]
                }
                """);

            string testAssetsDir = Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets");
            using Phase5Harness harness = new(GraphicsBackend.VulkanCompact, tempDir, testAssetsDir);
            using Texture output = harness.CreateOutput();
            World3DScene scene = harness.Scene;
            scene.Lighting = new World3DLighting
            {
                SunIntensity = 0f,
                SkyExposure = 0f,
                StarIntensity = 0f,
                SunDiscBrightness = 0f,
                AmbientFloor = 6f,
            };

            ModelAsset texturedModel = harness.Assets.Load<ModelAsset>("textured.amdl");
            ModelAsset customModel = harness.Assets.Load<ModelAsset>("custom.amdl");
            harness.PumpImagesUntilLoaded();

            Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
            Assert.That(harness.SceneAssets.TryInstantiateModel(
                texturedModel, sector, new World3DPosition(4.0, -2.0, 0.0), Quaternion.Identity, Vector3.One,
                out World3DModelInstance? leftInstance, lodMinimumDistances: [0f, 10f]), Is.True);
            Assert.That(harness.SceneAssets.TryInstantiateModel(
                customModel, sector, new World3DPosition(4.0, 2.0, 0.0), Quaternion.Identity, Vector3.One,
                out World3DModelInstance? rightInstance, lodMinimumDistances: [0f, 10f]), Is.True);

            // Upload queued LODs and render steady state.
            harness.SubmitFrame(output, s_originCamera);
            harness.SubmitFrame(output, s_originCamera);

            byte[] pixels = new byte[FrameSize * FrameSize * 4];
            harness.System.ReadTexture(output, pixels);

            ReadOnlySpan<byte> leftPx = PixelAt(pixels, 24, 48);
            Assert.That(leftPx[0], Is.GreaterThan(140), "Textured PbrStandard should output red channel from red.png.");
            Assert.That(leftPx[1], Is.LessThan(50));
            Assert.That(leftPx[2], Is.LessThan(50));

            // ParameterizedSurface modulates white with float3(0.25, 1.0, 0.75) * checker: green/cyan dominates red.
            (double rightR, double rightG, double rightB) = AveragePatchRgb(pixels, 72, 48, radius: 3);
            Assert.That(rightG, Is.GreaterThan(80.0), "Disk ParameterizedSurface should light green channel.");
            Assert.That(rightG, Is.GreaterThan(rightR * 1.8), "Disk ParameterizedSurface should suppress red relative to green.");
            Assert.That(rightB, Is.GreaterThan(rightR * 1.4), "Disk ParameterizedSurface should retain cyan/blue.");

            // Hot-reload textured material to bright blue emissive and verify readback changes.
            PbrMaterialAsset texturedPbr = (PbrMaterialAsset)harness.Assets.Load<MaterialAsset>("textured.amat");
            texturedPbr.BaseColorFactor = new Vector4(0.05f, 0.1f, 1.0f, 1f);
            texturedPbr.EmissiveFactor = new Vector3(0.0f, 0.15f, 2.5f);
            Assert.That(harness.SceneAssets.TryUpdateMaterial(texturedPbr), Is.True);
            harness.SubmitFrame(output, s_originCamera);
            harness.System.ReadTexture(output, pixels);
            ReadOnlySpan<byte> updatedLeftPx = PixelAt(pixels, 24, 48);
            Assert.That(updatedLeftPx[2], Is.GreaterThan(updatedLeftPx[0] + 80), "Hot-reloaded material must shift output to blue.");

            // Verify GPU LOD switch: place a single panel at x=16 (distance=16 >= minDistance=10) scaled x8 -> LOD1 covers lower half only.
            Assert.That(harness.SceneAssets.DestroyModelInstance(rightInstance!), Is.True);
            Assert.That(harness.SceneAssets.DestroyModelInstance(leftInstance!), Is.True);
            Assert.That(harness.SceneAssets.TryInstantiateModel(
                texturedModel, sector, new World3DPosition(16.0, 0.0, 0.0), Quaternion.Identity, new Vector3(1f, 8f, 8f),
                out World3DModelInstance? lodInstance, lodMinimumDistances: [0f, 10f]), Is.True);

            harness.SubmitFrame(output, s_originCamera);
            harness.SubmitFrame(output, s_originCamera);
            harness.System.ReadTexture(output, pixels);

            ReadOnlySpan<byte> topHalf = PixelAt(pixels, 48, 26);
            ReadOnlySpan<byte> bottomHalf = PixelAt(pixels, 48, 70);
            Assert.That(topHalf.ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 255 }),
                "At 16 m (>= 10 m), LOD1 omits upper half z > -0.05 so top pixel must stay black sky.");
            Assert.That(bottomHalf[2], Is.GreaterThan(100),
                "At 16 m (>= 10 m), LOD1 lower half must remain shaded.");
            Assert.That(harness.SceneAssets.DestroyModelInstance(lodInstance!), Is.True);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that attaching/detaching distant sectors or >48 local static GI instances does not force a global VoxelGI reset.
    /// </summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactDistantSectorStreaming_DoesNotResetVoxelGiClipmap()
    {
        using Phase5Harness harness = new(GraphicsBackend.VulkanCompact, assetsRoot: null);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        scene.VoxelGi.Enabled = true;

        World3DCamera camera = new(new World3DPosition(120_000.0, -80_000.0, 10.0), Quaternion.Identity, MathF.PI / 2, 0.125f);
        Assert.That(scene.TryRegisterGeometry(new Vector3(-2f), new Vector3(2f), out WorldGeometryHandle boxGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(boxGeom, 0, CreateCubeVertices(), CreateCubeIndices()), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.8f, 0.8f, 0.8f, 1f) }, out WorldMaterialHandle mat), Is.True);
        Assert.That(scene.TryAttachSector(out WorldSectorHandle nearSector), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(nearSector, boxGeom, mat,
            new World3DTransform(new World3DPosition(120_008.0, -80_000.0, 8.0), Quaternion.Identity, Vector3.One)), out _), Is.True);

        // Commit initial VoxelGI clipmap cascades.
        harness.SubmitFrame(output, camera);
        harness.SubmitFrame(output, camera);
        Assert.That(scene.LastRecordedVoxelGi.ResetCascadeMask, Is.Zero, "Steady camera after commit must have zero resets.");

        // Add a distant sector 600 m away (> Cascade 3 +/-128m range) with 80 static GI instances (> 48).
        Assert.That(scene.TryAttachSector(out WorldSectorHandle farSector), Is.True);
        for (int i = 0; i < 80; ++i)
        {
            World3DTransform xform = new(new World3DPosition(120_600.0 + (i % 10) * 6.0, -80_000.0 + (i / 10) * 6.0, 10.0),
                Quaternion.Identity, Vector3.One);
            Assert.That(scene.TryAddInstance(new World3DInstanceDesc(farSector, boxGeom, mat, xform), out _), Is.True);
        }

        harness.SubmitFrame(output, camera);
        Assert.Multiple(() =>
        {
            Assert.That(scene.LastRecordedVoxelGi.ResetCascadeMask, Is.Zero,
                "Attaching >48 static instances outside VoxelGI range must NOT wipe clipmap cascades.");
            Assert.That(scene.LastRecordedVoxelGi.EvictedStaticBricksThisFrame, Is.Zero);
        });

        Assert.That(scene.TryDetachSector(farSector), Is.True);
        harness.SubmitFrame(output, camera);
        Assert.That(scene.LastRecordedVoxelGi.ResetCascadeMask, Is.Zero,
            "Detaching distant sector must NOT wipe clipmap cascades.");

        // Add 60 in-range static instances (exceeds 48 cap -> should merge dirty bounds rather than reset all cascades).
        Assert.That(scene.TryAttachSector(out WorldSectorHandle denseLocalSector), Is.True);
        for (int i = 0; i < 60; ++i)
        {
            World3DTransform xform = new(new World3DPosition(120_010.0 + (i % 6) * 2.0, -80_005.0 + (i / 6) * 2.0, 10.0),
                Quaternion.Identity, Vector3.One * 0.5f);
            Assert.That(scene.TryAddInstance(new World3DInstanceDesc(denseLocalSector, boxGeom, mat, xform), out _), Is.True);
        }
        harness.SubmitFrame(output, camera);
        Assert.That(scene.LastRecordedVoxelGi.ResetCascadeMask, Is.Zero,
            "Overflowing >48 local dirty bounds should merge cleanly without forcing a global cascade reset.");
    }

    /// <summary>
    /// Verifies that multi-kilometer sector streaming churn leaks zero scene handles, geometry pages, or <see cref="RenderingSystem"/> VRAM/slots.
    /// </summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactMultiKilometerStreamingChurn_ZeroGpuMemoryOrSlotLeak()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "alco_w3d_p5_churn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteSolidPng(Path.Combine(tempDir, "albedo.png"), 180, 140, 110, 255);
            WriteMultiLodPanelMesh(Path.Combine(tempDir, "chunk.amsh"));
            File.WriteAllText(Path.Combine(tempDir, "stone.amat"), """
                {
                    "$type": "Alco.World3D.PbrMaterialAsset",
                    "version": "1.0",
                    "roughnessFactor": 0.65,
                    "textures": { "baseColor": "albedo.png" }
                }
                """);
            File.WriteAllText(Path.Combine(tempDir, "chunk.amdl"), """
                {
                    "version": "1.0",
                    "mesh": "chunk.amsh",
                    "slots": [ { "name": "panel", "material": "stone.amat" } ]
                }
                """);

            using Phase5Harness harness = new(GraphicsBackend.VulkanCompact, tempDir);
            using Texture output = harness.CreateOutput();
            World3DScene scene = harness.Scene;
            World3DSceneAssets sceneAssets = harness.SceneAssets;
            scene.Occlusion.Enabled = true;
            scene.Occlusion.TwoPhase = true;
            scene.Shadows.Enabled = true;
            scene.VoxelGi.Enabled = true;
            scene.Forward.Enabled = true;
            scene.Ssr.Enabled = true;

            ModelAsset chunkModel = harness.Assets.Load<ModelAsset>("chunk.amdl");
            harness.PumpImagesUntilLoaded();

            // Warm up RenderGraph passes and table/directory allocations so steady-state baseline captures pass targets.
            Assert.That(scene.TryAttachSector(out WorldSectorHandle warmSector), Is.True);
            Assert.That(sceneAssets.TryInstantiateModel(
                chunkModel, warmSector, new World3DPosition(100_005.0, 50_000.0, 10.0), Quaternion.Identity, Vector3.One,
                out World3DModelInstance? warmInst), Is.True);
            harness.SubmitFrame(output, new World3DCamera(new World3DPosition(100_000.0, 50_000.0, 10.0), Quaternion.Identity, 1.0f, 0.1f));
            harness.SubmitFrame(output, new World3DCamera(new World3DPosition(100_000.0, 50_000.0, 10.0), Quaternion.Identity, 1.0f, 0.1f));

            Assert.That(sceneAssets.DestroyModelInstance(warmInst!), Is.True);
            Assert.That(scene.TryDetachSector(warmSector), Is.True);
            sceneAssets.ReclaimReleased();
            for (int f = 0; f < 4; ++f)
                harness.SubmitFrame(output, new World3DCamera(new World3DPosition(100_000.0, 50_000.0, 10.0), Quaternion.Identity, 1.0f, 0.1f));
            harness.System.WaitIdle();
            sceneAssets.ReclaimReleased();
            harness.System.Tick();

            // Epoch 1 establishes steady-state high-water capacity across growable cull/draw/table buffers.
            void RunStreamingEpoch(double startX, int sectorCount)
            {
                Queue<(WorldSectorHandle Sector, List<World3DModelInstance> Instances)> active = new();
                for (int step = 0; step < sectorCount; ++step)
                {
                    double baseX = startX + step * 125.0;
                    World3DCamera cam = new(new World3DPosition(baseX, 50_000.0, 12.0), Quaternion.Identity, 1.0f, 0.1f);
                    Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
                    List<World3DModelInstance> instances = new(6);
                    for (int j = 0; j < 6; ++j)
                    {
                        World3DPosition pos = new(baseX + 12.0 + (j % 3) * 18.0, 50_000.0 + (j / 3 - 0.5) * 12.0, 10.0);
                        Assert.That(sceneAssets.TryInstantiateModel(chunkModel, sector, pos, Quaternion.Identity, Vector3.One * 3f,
                            out World3DModelInstance? inst), Is.True);
                        instances.Add(inst!);
                    }
                    active.Enqueue((sector, instances));

                    while (active.Count > 3)
                    {
                        (WorldSectorHandle oldSector, List<World3DModelInstance> oldInsts) = active.Dequeue();
                        foreach (World3DModelInstance inst in oldInsts)
                            Assert.That(sceneAssets.DestroyModelInstance(inst), Is.True);
                        Assert.That(scene.TryDetachSector(oldSector), Is.True);
                    }

                    harness.SubmitFrame(output, cam);
                    sceneAssets.ReclaimReleased();
                }

                while (active.TryDequeue(out (WorldSectorHandle Sector, List<World3DModelInstance> Instances) item))
                {
                    foreach (World3DModelInstance inst in item.Instances)
                        Assert.That(sceneAssets.DestroyModelInstance(inst), Is.True);
                    Assert.That(scene.TryDetachSector(item.Sector), Is.True);
                }

                sceneAssets.ReclaimReleased();
                World3DCamera endCam = new(new World3DPosition(startX + sectorCount * 125.0, 50_000.0, 12.0), Quaternion.Identity, 1.0f, 0.1f);
                for (int f = 0; f < 4; ++f)
                    harness.SubmitFrame(output, endCam);
                harness.System.WaitIdle();
                sceneAssets.ReclaimReleased();
                harness.System.Tick();
            }

            RunStreamingEpoch(100_000.0, 20);
            World3DSceneResourceStats baseScene = scene.GetResourceStats();
            RenderingMemoryStats baseMem = harness.System.GetMemoryStats();
            Assert.Multiple(() =>
            {
                Assert.That(baseScene.LiveSectors, Is.Zero);
                Assert.That(baseScene.LiveInstances, Is.Zero);
                Assert.That(baseScene.LiveMaterials, Is.Zero);
                Assert.That(baseScene.LiveGeometries, Is.Zero);
                Assert.That(baseScene.LiveTextures, Is.Zero);
                Assert.That(baseScene.LiveGeometryPages, Is.Zero);
            });

            // Epoch 2 streams another 2.5 km; post-teardown scene and VRAM stats must match Epoch 1 exactly.
            RunStreamingEpoch(102_500.0, 20);
            World3DSceneResourceStats endScene = scene.GetResourceStats();
            RenderingMemoryStats endMem = harness.System.GetMemoryStats();

            Assert.Multiple(() =>
            {
                Assert.That(endScene.LiveSectors, Is.Zero);
                Assert.That(endScene.LiveInstances, Is.Zero);
                Assert.That(endScene.LiveMaterials, Is.Zero);
                Assert.That(endScene.LiveGeometries, Is.Zero);
                Assert.That(endScene.LiveTextures, Is.Zero);
                Assert.That(endScene.LiveGeometryPages, Is.Zero);
                Assert.That(endScene.LiveGeometryPageBytes, Is.Zero);
                Assert.That(endScene.PendingLodUploads, Is.Zero);
                Assert.That(endScene.PendingRetirementBatches, Is.Zero);

                Assert.That(endMem.LiveBuffers, Is.EqualTo(baseMem.LiveBuffers), "Leaked buffers after churn teardown.");
                Assert.That(endMem.LiveTextures, Is.EqualTo(baseMem.LiveTextures), "Leaked textures after churn teardown.");
                Assert.That(endMem.LiveSamplers, Is.EqualTo(baseMem.LiveSamplers), "Leaked samplers after churn teardown.");
                Assert.That(endMem.GpuBufferPool.UsedBytes, Is.EqualTo(baseMem.GpuBufferPool.UsedBytes), "Leaked GPU buffer pool bytes.");
                Assert.That(endMem.CpuBufferPool.UsedBytes, Is.EqualTo(baseMem.CpuBufferPool.UsedBytes), "Leaked CPU buffer pool bytes.");
                Assert.That(endMem.TexturePool.UsedBytes, Is.EqualTo(baseMem.TexturePool.UsedBytes), "Leaked texture pool bytes.");
            });
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private static ReadOnlySpan<byte> PixelAt(byte[] pixels, int x, int y) => pixels.AsSpan((y * FrameSize + x) * 4, 4);

    private static (double R, double G, double B) AveragePatchRgb(byte[] pixels, int cx, int cy, int radius)
    {
        double r = 0, g = 0, b = 0;
        int count = 0;
        for (int dy = -radius; dy <= radius; ++dy)
        {
            for (int dx = -radius; dx <= radius; ++dx)
            {
                ReadOnlySpan<byte> px = PixelAt(pixels, cx + dx, cy + dy);
                r += px[0];
                g += px[1];
                b += px[2];
                ++count;
            }
        }
        return (r / count, g / count, b / count);
    }

    private static void WriteSolidPng(string path, byte r, byte g, byte b, byte a)
    {
        const int size = 8;
        byte[] rgba = new byte[size * size * 4];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = b;
            rgba[i + 3] = a;
        }
        File.WriteAllBytes(path, ImageEncodeUtility.EncodePng(rgba, size, size));
    }

    private static void WriteMultiLodPanelMesh(string path)
    {
        // Clockwise when viewed from -X looking +X.
        VertexPBR[] lod0 =
        [
            new(new Vector3(0f, -1f, -1f), -Vector3.UnitX, new Vector2(0f, 1f)),
            new(new Vector3(0f, -1f,  1f), -Vector3.UnitX, new Vector2(0f, 0f)),
            new(new Vector3(0f,  1f,  1f), -Vector3.UnitX, new Vector2(1f, 0f)),
            new(new Vector3(0f,  1f, -1f), -Vector3.UnitX, new Vector2(1f, 1f)),
        ];
        // LOD1 covers only the lower half (z in [-1, -0.05]) so upper screen center reveals sky after switching.
        VertexPBR[] lod1 =
        [
            new(new Vector3(0f, -1f, -1.00f), -Vector3.UnitX, new Vector2(0f, 1f)),
            new(new Vector3(0f, -1f, -0.05f), -Vector3.UnitX, new Vector2(0f, 0f)),
            new(new Vector3(0f,  1f, -0.05f), -Vector3.UnitX, new Vector2(1f, 0f)),
            new(new Vector3(0f,  1f, -1.00f), -Vector3.UnitX, new Vector2(1f, 1f)),
        ];

        MeshAssetBuildData data = new()
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Streams = MeshVertexLayout.CreatePBR(),
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = ToVertexBytes(lod0),
                    Indices = [0u, 1u, 2u, 0u, 2u, 3u],
                    MaxError = 0f,
                    SubMeshes = [new MeshSubMeshMeta("panel", 0, 6)],
                },
                new MeshAssetBuildLod
                {
                    Vertices = ToVertexBytes(lod1),
                    Indices = [0u, 1u, 2u, 0u, 2u, 3u],
                    MaxError = 1f,
                    SubMeshes = [new MeshSubMeshMeta("panel", 0, 6)],
                },
            ],
        };
        using FileStream fs = File.Create(path);
        MeshAssetWriter.Write(data, fs);
    }

    private static byte[] ToVertexBytes(VertexPBR[] vertices)
    {
        byte[] bytes = new byte[vertices.Length * VertexPBR.SizeInBytes];
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertices.AsSpan()).CopyTo(bytes);
        return bytes;
    }

    private static VertexPBR[] CreateCubeVertices()
    {
        VertexPBR[] vertices = new VertexPBR[24];
        ReadOnlySpan<Vector3> normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        for (int face = 0; face < normals.Length; face++)
        {
            Vector3 normal = normals[face];
            Vector3 u = MathF.Abs(normal.Z) > 0.5f ? Vector3.UnitX : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, normal));
            Vector3 v = Vector3.Cross(normal, u);
            Vector4 tangent = new(u, 1f);
            int idx = face * 4;
            vertices[idx] = new VertexPBR(normal - u - v, normal, Vector2.Zero, tangent);
            vertices[idx + 1] = new VertexPBR(normal + u - v, normal, Vector2.UnitX, tangent);
            vertices[idx + 2] = new VertexPBR(normal - u + v, normal, Vector2.UnitY, tangent);
            vertices[idx + 3] = new VertexPBR(normal + u + v, normal, Vector2.One, tangent);
        }
        return vertices;
    }

    private static uint[] CreateCubeIndices()
    {
        uint[] indices = new uint[36];
        for (int face = 0; face < 6; face++)
        {
            uint v = (uint)(face * 4);
            int i = face * 6;
            indices[i] = v;
            indices[i + 1] = v + 1;
            indices[i + 2] = v + 2;
            indices[i + 3] = v + 1;
            indices[i + 4] = v + 3;
            indices[i + 5] = v + 2;
        }
        return indices;
    }

    private sealed class TestAssetHost : IAssetSystemHost, IDisposable
    {
        public event Action? OnDispose;
        public void PostToMainThread(Action action) => action();
        public void LogInfo(ReadOnlySpan<char> message) { }
        public void LogWarning(ReadOnlySpan<char> message) { }
        public void LogError(ReadOnlySpan<char> message) { }
        public void LogSuccess(ReadOnlySpan<char> message) { }
        public void Dispose() => OnDispose?.Invoke();
    }

    private sealed class Phase5Harness : IDisposable
    {
        private readonly GPUDevice _device;
        private readonly TestAssetHost _assetHost = new();
        private readonly ImageUploadQueue _uploadQueue;

        public Phase5Harness(GraphicsBackend backend, string? assetsRoot, string? secondaryRoot = null)
        {
            _device = CreateDevice(backend);
            RenderingSystem? system = null;
            RenderContext? context = null;
            ImageUploadQueue? uploadQueue = null;
            World3DScene? scene = null;
            try
            {
                AssetSystem assets = new(_assetHost);
                if (!string.IsNullOrEmpty(assetsRoot))
                    assets.AddFileSource(new DirectoryFileSource(assetsRoot));
                if (!string.IsNullOrEmpty(secondaryRoot))
                    assets.AddFileSource(new DirectoryFileSource(secondaryRoot));

                (ShaderFileResolver resolver, ShaderPathExists exists) = ShaderModuleResolver.CreateIndexed(
                    path => assets.TryGetStream(path, out Stream? s) ? s : null,
                    () => assets.AllAssetNames,
                    () => assets.Version);

                System = system = new RenderingSystem(_device,
                    textureHeapByteCount: 16 * 1024 * 1024,
                    bufferHeapByteCount: 16 * 1024 * 1024,
                    uploadStagingByteCount: 2 * 1024 * 1024,
                    shaderOptions: new ShaderCompilerOptions { FileResolver = resolver, PathExists = exists });
                Context = context = new RenderContext(system);
                _uploadQueue = uploadQueue = new ImageUploadQueue(system);

                if (backend == GraphicsBackend.NoGPU)
                    assets.RegisterAssetLoader(new AssetLoaderImageNoGPU(system, assets));
                else
                    assets.RegisterAssetLoader(new AssetLoaderImage(uploadQueue));
                assets.RegisterAssetLoader(new AssetLoaderMaterialAsset(assets));
                World3DAssetPipeline.RegisterLoaders(assets);
                Assets = assets;

                Scene = scene = new World3DScene(system);
                scene.Occlusion.Enabled = false;
                scene.Shadows.Enabled = false;
                scene.VoxelGi.Enabled = false;
                scene.Forward.Enabled = false;
                scene.VolumetricClouds.Enabled = false;
                scene.VolumetricLight.Enabled = false;
                scene.Ssr.Enabled = false;
                SceneAssets = new World3DSceneAssets(scene);
            }
            catch
            {
                scene?.Dispose();
                uploadQueue?.Dispose();
                _assetHost.Dispose();
                context?.Dispose();
                system?.Dispose();
                _device.Destroy();
                throw;
            }
        }

        public RenderingSystem System { get; }
        public RenderContext Context { get; }
        public AssetSystem Assets { get; }
        public World3DScene Scene { get; }
        public World3DSceneAssets SceneAssets { get; }

        public Texture CreateOutput() => System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(FrameSize, FrameSize, 1),
            Format = Format.Rgba8Srgb,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });

        public void PumpImagesUntilLoaded()
        {
            while (_uploadQueue.HasPendingWork)
            {
                _uploadQueue.Pump();
                Thread.Sleep(1);
            }
        }

        public void SubmitFrame(Texture? output = null, World3DCamera? camera = null)
        {
            Context.BeginFrame();
            RenderGraphTarget target = output == null ? default : RenderGraphTarget.FromTexture(output);
            RGUsage usage = output == null ? RGUsage.None : RGUsage.TransferRead;
            Assert.That(Scene.TryRecordFrame(Context, camera ?? s_originCamera, new Extent2D(FrameSize, FrameSize),
                out _, target, usage, renderWorld: true), Is.True);
            Context.SubmitWithCompletion().Wait();
        }

        public void Dispose()
        {
            Context.DiscardFrame();
            SceneAssets.Dispose();
            Scene.Dispose();
            Context.Dispose();
            _uploadQueue.Dispose();
            _assetHost.Dispose();
            System.Dispose();
            _device.Destroy();
        }

        private static GPUDevice CreateDevice(GraphicsBackend backend)
        {
            try
            {
                return Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true });
            }
            catch (GPUException ex) when (backend == GraphicsBackend.VulkanCompact && ex.Error == Error.Unsupported)
            {
                Assert.Ignore($"VulkanCompact is unsupported on this host: {ex.Message}");
                throw;
            }
        }
    }
}
