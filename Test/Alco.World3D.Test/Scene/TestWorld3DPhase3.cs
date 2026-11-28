using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>Exercises Phase 3 zero-CPU-BVH GPU-driven VoxelGI: clipmap math, shaders, Cornell bounce, custom surfaces, dynamic geo, RSM/lights, 500 km stability, and fly stress.</summary>
[TestFixture]
public sealed class TestWorld3DPhase3
{
    private const string CustomSurfaceModule = "TestPhase3CustomSurface";
    private const string CustomSurfaceSource = """
        #language slang 2025
        module TestPhase3CustomSurface;
        import World3DSurface;

        public struct TestPhase3CustomSurface : ISurface
        {
            public override float4 getBaseColor(SurfaceInput input)
            {
                return float4(0.15, 0.85, 0.95, 1.0);
            }

            public override float3 getEmissive(SurfaceInput input)
            {
                return float3(0.2, 4.5, 4.5);
            }

            public override float4 getVoxelBaseColor(VoxelSurfaceInput input)
            {
                return float4(0.15, 0.85, 0.95, 1.0);
            }

            public override float3 getVoxelEmissive(VoxelSurfaceInput input)
            {
                return float3(0.2, 4.5, 4.5);
            }
        };
        """;

    private static readonly Vector3 s_boxMin = new(-1f, -1f, -1f);
    private static readonly Vector3 s_boxMax = new(1f, 1f, 1f);
    private static readonly VertexPBR[] s_boxVertices = CreateBoxVertices();
    private static readonly uint[] s_boxIndices = CreateBoxIndices();

    /// <summary>Compiles all 16 Phase 3 shaders and composed ISurface/IVoxelFeedSurface voxelizers across every target.</summary>
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Dxil)]
    [TestCase(ShaderTarget.MetalSource)]
    public void ValidateShader_CompilesPhase3KernelsAndComposedVoxelizer(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
            Assert.Ignore($"The bundled shader compiler cannot emit {target} on this host.");

        using ShaderCompiler compiler = new(target, new ShaderCompilerOptions { SearchPaths = [] });
        ReadOnlySpan<string> modules =
        [
            "World3DSceneData",
            "World3DSurface",
            "World3DPbrStandard",
            "World3DOcclusionCommon",
            "World3DShadowSample",
            "World3DVoxelCommon",
            "World3DVoxelInit",
            "World3DVoxelBlueNoise",
            "World3DVoxelPageManage",
            "World3DVoxelCullInstances",
            "World3DVoxelPrepareArgs",
            "World3DVoxelExpandChunks",
            "World3DVoxelClear",
            "World3DVoxelVoxelize",
            "World3DVoxelInject",
            "World3DVoxelMip",
            "World3DVoxelMipChain",
            "World3DVoxelPropagate",
            "World3DVoxelTrace",
            "World3DVoxelDemosaic",
            "World3DVoxelUpsample",
        ];
        foreach (string module in modules)
        {
            string file = module + ".slang";
            using StreamReader reader = new(typeof(World3DScene).Assembly.GetManifestResourceStream(file)
                ?? throw new InvalidOperationException($"Missing embedded shader {file}."));
            compiler.RegisterVirtualModule(module, file, reader.ReadToEnd());
        }
        compiler.RegisterVirtualModule(CustomSurfaceModule, CustomSurfaceModule + ".slang", CustomSurfaceSource);

        IReadOnlyList<ShaderTypeLayout> commonTypes = compiler.LoadModuleFromSource(
            "World3DVoxelCommon", "World3DVoxelCommon.slang",
            new StreamReader(typeof(World3DScene).Assembly.GetManifestResourceStream("World3DVoxelCommon.slang")!).ReadToEnd()).GetTypes();
        Assert.That(commonTypes.First(t => t.TypeName == "World3DVoxelCascadeGpu").SizeBytes, Is.EqualTo(32u));
        Assert.That(commonTypes.First(t => t.TypeName == "World3DVoxelEvictBoxGpu").SizeBytes, Is.EqualTo(32u));
        Assert.That(commonTypes.First(t => t.TypeName == "World3DVoxelCandidateGpu").SizeBytes, Is.EqualTo(16u));
        Assert.That(commonTypes.First(t => t.TypeName == "World3DVoxelChunkGpu").SizeBytes, Is.EqualTo(8u));
        Assert.That(commonTypes.First(t => t.TypeName == "World3DVoxelCountersGpu").SizeBytes, Is.EqualTo(40u));
        Assert.That(commonTypes.First(t => t.TypeName == "World3DVoxelFrameGpu").SizeBytes, Is.EqualTo(496u));

        ReadOnlySpan<(string Module, uint RootBytes)> kernels =
        [
            ("World3DVoxelInit", 80),
            ("World3DVoxelBlueNoise", 16),
            ("World3DVoxelPageManage", 16),
            ("World3DVoxelCullInstances", 96),
            ("World3DVoxelPrepareArgs", 96),
            ("World3DVoxelExpandChunks", 32),
            ("World3DVoxelClear", 44),
            ("World3DVoxelInject", 48),
            ("World3DVoxelMip", 24),
            ("World3DVoxelMipChain", 32),
            ("World3DVoxelPropagate", 88),
            ("World3DVoxelTrace", 64),
            ("World3DVoxelDemosaic", 80),
            ("World3DVoxelUpsample", 48),
        ];
        foreach ((string module, uint rootBytes) in kernels)
            Assert.That(compiler.Compile(module, ShaderPipelineKind.Compute).Reflection.Root?.SizeBytes, Is.EqualTo(rootBytes), module);

        ReadOnlySpan<string> surfaces = ["World3DPbrStandard", CustomSurfaceModule];
        foreach (string surface in surfaces)
        {
            CompiledShader composed = compiler.CompileComposed("World3DVoxelVoxelize", surface, ShaderPipelineKind.Compute);
            Assert.That(composed.Reflection.Root?.SizeBytes, Is.EqualTo(136u), $"Voxelize+{surface}");
        }
    }

    /// <summary>Verifies O(1) 64-bit clipmap scrolling, exact disjoint eviction counts, hysteresis, rollback, and 500 km anchor precision.</summary>
    [Test]
    public void NoGpuClipmap_ScrollsInO1ProducesDisjointEvictBoxesAndMaintains500KmPrecision()
    {
        World3DVoxelClipmap clipmap = new();
        clipmap.Plan(new World3DPosition(0, 0, 0), new World3DCell(0, 0, 0), 128, 0.25f, forceReset: false, out var resetPlan);
        Assert.That(resetPlan.BricksPerAxis, Is.EqualTo(16));
        Assert.That(resetPlan.ResetCascadeMask, Is.EqualTo(0b1111));
        Assert.That(resetPlan.EvictBoxCount, Is.EqualTo(4));
        Assert.That(resetPlan.EvictedBrickCount, Is.EqualTo(4 * 4096));
        clipmap.Commit();

        // Sub-brick hysteresis move: 0.4m is < 1 brick (2.0m for C0) -> zero eviction or scroll.
        clipmap.Plan(new World3DPosition(0.4, -0.3, 0.2), new World3DCell(0, 0, 0), 128, 0.25f, forceReset: false, out var wigglePlan);
        Assert.That(wigglePlan.ResetCascadeMask, Is.EqualTo(0));
        Assert.That(wigglePlan.ShiftedCascadeMask, Is.EqualTo(0));
        Assert.That(wigglePlan.EvictedBrickCount, Is.EqualTo(0));
        clipmap.Rollback();

        // Scroll by (+2.1m, -4.1m, +2.1m): C0 (2m brick) shifts (1, -2, 1), C1 (4m brick) shifts (0, -1, 0).
        clipmap.Plan(new World3DPosition(2.1, -4.1, 2.1), new World3DCell(0, 0, 0), 128, 0.25f, forceReset: false, out var scrollPlan);
        Assert.That(scrollPlan.ResetCascadeMask, Is.EqualTo(0));
        Assert.That(scrollPlan.ShiftedCascadeMask, Is.EqualTo(0b0011));
        const int b = 16;
        int expectedC0 = b * b * b - (b - 1) * (b - 2) * (b - 1);
        int expectedC1 = b * b * b - b * (b - 1) * b;
        Assert.That(scrollPlan.EvictedBrickCount, Is.EqualTo(expectedC0 + expectedC1));

        // Rollback drops pending shifts without altering committed origin.
        clipmap.Rollback();
        clipmap.Plan(new World3DPosition(0, 0, 0), new World3DCell(0, 0, 0), 128, 0.25f, forceReset: false, out var postRollbackPlan);
        Assert.That(postRollbackPlan.EvictedBrickCount, Is.EqualTo(0));
        clipmap.Commit();

        // 500 km teleport resets cleanly and emits anchor-relative origins bounded within 1 cell.
        World3DPosition farPos = new(500_000.0, -320_000.0, 120.0);
        Assert.That(World3DSceneCoordinates.TrySplitPosition(farPos, out World3DCell farCell, out _), Is.True);
        clipmap.Plan(farPos, farCell, 128, 0.25f, forceReset: false, out var farPlan);
        Assert.That(farPlan.ResetCascadeMask, Is.EqualTo(0b1111));
        for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
        {
            Vector3 rel = new(farPlan.Cascades[c].OriginVoxelSize.X, farPlan.Cascades[c].OriginVoxelSize.Y, farPlan.Cascades[c].OriginVoxelSize.Z);
            Assert.That(float.IsFinite(rel.X) && float.IsFinite(rel.Y) && float.IsFinite(rel.Z), Is.True);
            Assert.That(rel.Length(), Is.LessThan(1200f));
        }
        clipmap.Commit();

        // Small step at 500 km scrolls smoothly without resetting.
        clipmap.Plan(new World3DPosition(farPos.X + 2.1, farPos.Y, farPos.Z), farCell, 128, 0.25f, forceReset: false, out var farScrollPlan);
        Assert.That(farScrollPlan.ResetCascadeMask, Is.EqualTo(0));
        Assert.That(farScrollPlan.ShiftedCascadeMask, Is.EqualTo(1));
    }

    /// <summary>Verifies GPU freelist allocation, empty-air reclamation, rollback safety, and zero pool overflow across scrolling frames.</summary>
    [Test]
    public void VulkanGpuFreelist_ReclaimsEmptyAirAndRespectsBudgetAcrossScrolling()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture output = harness.CreateOutput(192, 192);
        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = false;
        scene.VoxelGi.Resolution = 64;
        scene.VoxelGi.BaseVoxelSize = 0.5f;
        scene.VoxelGi.StaticBrickPoolCapacity = 1024;
        scene.VoxelGi.DynamicBrickPoolCapacity = 256;
        scene.VoxelGi.StaticBrickBudgets = [256, 128, 64, 64];
        scene.VoxelGi.BootstrapFullVolumeOnReset = false;

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle boxGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(boxGeom, 0, s_boxVertices, s_boxIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.8f, 0.8f, 0.8f, 1f) }, out WorldMaterialHandle mat), Is.True);
        for (int i = 0; i < 16; ++i)
        {
            Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, boxGeom, mat,
                TransformAt(i * 4.0, 0.0, 0.0, 1.0f)), out _), Is.True);
        }

        // Warm upload and settle.
        for (int f = 0; f < 6; ++f)
            harness.SubmitFrame(output, LookAt(new Vector3(-6f + f * 3.5f, 0f, 3f), new Vector3(f * 3.5f + 4f, 0f, 0f)));

        Assert.That(scene.TryReadVoxelGiCounters(out World3DVoxelGiGpuCounters initialCounters), Is.True);
        Assert.That(initialCounters.StaticUsedBricks, Is.GreaterThan(0u));
        Assert.That(initialCounters.StaticUsedBricks, Is.LessThan(512u), "Empty sky bricks must be reclaimed to freelist.");
        Assert.That(initialCounters.StaticFallbackBricks, Is.EqualTo(0u));

        // Record & discard one scrolled frame, then resume scrolling and confirm zero leaks.
        World3DCamera discardCam = LookAt(new Vector3(50f, 0f, 3f), new Vector3(55f, 0f, 0f));
        harness.RecordFrame(output, discardCam);
        harness.Context.DiscardFrame();

        uint totalReclaimed = 0;
        for (int f = 0; f < 16; ++f)
        {
            harness.SubmitFrame(output, LookAt(new Vector3(f * 3.0f, 0f, 3f), new Vector3(f * 3.0f + 8f, 0f, 0f)));
            Assert.That(scene.TryReadVoxelGiCounters(out World3DVoxelGiGpuCounters c), Is.True);
            totalReclaimed += c.StaticReclaimedThisFrame;
            Assert.That(c.StaticFallbackBricks, Is.EqualTo(0u));
            Assert.That(c.StaticUsedBricks, Is.LessThan(512u));
        }
        Assert.That(totalReclaimed, Is.GreaterThan(0u));
    }

    /// <summary>Verifies GI diffuse bounce, red/green Cornell color bleeding, emissive illumination, multi-bounce, and GI debug views.</summary>
    [Test]
    public void VulkanGpuCornellBox_ProducesDiffuseColorBleedEmissiveAndMultiBounceGi()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        const int size = 192;
        using Texture outGiOff = harness.CreateOutput(size, size);
        using Texture outGiOn = harness.CreateOutput(size, size);
        using Texture outNoBounce = harness.CreateOutput(size, size);
        using Texture outDbgDiff = harness.CreateOutput(size, size);
        using Texture outDbgSpec = harness.CreateOutput(size, size);
        using Texture outDbgVis = harness.CreateOutput(size, size);
        using Texture outDbgRaw = harness.CreateOutput(size, size);
        using Texture outHalfRes = harness.CreateOutput(size, size);

        World3DScene scene = harness.Scene;
        Assert.That(scene.VoxelGi.HalfResolutionTrace, Is.False, "VoxelGI should default to full-resolution tracing.");
        scene.Shadows.Enabled = false;
        scene.Lighting = scene.Lighting with { SunIntensity = 0f, SkyExposure = 0f, AmbientFloor = 0f };
        scene.VoxelGi.Resolution = 64;
        scene.VoxelGi.BaseVoxelSize = 0.25f;
        scene.VoxelGi.StaticBrickPoolCapacity = 2048;
        scene.VoxelGi.DynamicBrickPoolCapacity = 512;
        scene.VoxelGi.BootstrapFullVolumeOnReset = true;
        scene.VoxelGi.IndirectDiffuseIntensity = 1.5f;

        BuildCornellScene(scene, Vector3.Zero, includeInteriorCrate: true);
        World3DCamera cam = LookAt(new Vector3(-2.6f, 0f, 2.2f), new Vector3(2.0f, 0f, 1.8f));

        // 1. GI disabled reference: shadowed dark interior without emissive GI bounce.
        scene.VoxelGi.Enabled = false;
        harness.SubmitFrame(outGiOff, cam);
        byte[] giOffPx = ReadPixels(harness.System, outGiOff, size);

        // 2. GI enabled (multi-bounce): settle 3 frames for temporal accumulation.
        scene.VoxelGi.Enabled = true;
        scene.VoxelGi.PropagationDecay = 0.9f;
        for (int i = 0; i < 3; ++i)
            harness.SubmitFrame(outGiOn, cam);
        byte[] giOnPx = ReadPixels(harness.System, outGiOn, size);

        // Sample white floor strip in lower middle of viewport (must be dark with GI off, bright with GI on).
        double avgFloorOff = AverageRegionLuma(giOffPx, size, size / 2 - 12, size / 2 + 12, (int)(size * 0.72), (int)(size * 0.85));
        double avgFloorOn = AverageRegionLuma(giOnPx, size, size / 2 - 12, size / 2 + 12, (int)(size * 0.72), (int)(size * 0.85));
        Assert.That(avgFloorOn, Is.GreaterThan(avgFloorOff + 18.0), "Emissive ceiling GI must illuminate shaded white Cornell floor.");

        // Check red (+Y right half) vs green (-Y left half) indirect color bleeding on white back wall strips clear of the crate.
        (double leftR, double leftG, _) = AverageRegionRgb(giOnPx, size, (int)(size * 0.06), (int)(size * 0.18), (int)(size * 0.28), (int)(size * 0.52));
        (double rightR, double rightG, _) = AverageRegionRgb(giOnPx, size, (int)(size * 0.82), (int)(size * 0.94), (int)(size * 0.28), (int)(size * 0.52));
        Assert.That(leftG, Is.GreaterThan(leftR + 8.0), "Left white region next to green wall must show green bounce.");
        Assert.That(rightR, Is.GreaterThan(rightG + 8.0), "Right white region next to red wall must show red bounce.");

        // 3. Compare single-bounce (decay = 0) vs multi-bounce (decay = 0.9).
        scene.VoxelGi.PropagationDecay = 0f;
        for (int i = 0; i < 3; ++i)
            harness.SubmitFrame(outNoBounce, cam);
        byte[] noBouncePx = ReadPixels(harness.System, outNoBounce, size);
        double avgNoBounce = AverageRegionLuma(noBouncePx, size, size / 2 - 12, size / 2 + 12, (int)(size * 0.72), (int)(size * 0.85));
        Assert.That(avgFloorOn, Is.GreaterThan(avgNoBounce + 1.0), "Multi-bounce propagation must increase indirect illumination.");

        // 4. Verify GI debug views produce non-trivial output.
        scene.VoxelGi.PropagationDecay = 0.9f;
        scene.LightingDebugView = World3DLightingDebugView.GiDiffuse;
        harness.SubmitFrame(outDbgDiff, cam);
        scene.LightingDebugView = World3DLightingDebugView.GiSpecular;
        harness.SubmitFrame(outDbgSpec, cam);
        scene.LightingDebugView = World3DLightingDebugView.GiVisibility;
        harness.SubmitFrame(outDbgVis, cam);
        scene.LightingDebugView = World3DLightingDebugView.GiRawDiffuseTrace;
        harness.SubmitFrame(outDbgRaw, cam);

        Assert.That(MaxChannel(ReadPixels(harness.System, outDbgDiff, size)), Is.GreaterThan(20));
        Assert.That(MaxChannel(ReadPixels(harness.System, outDbgSpec, size)), Is.GreaterThan(10));
        Assert.That(MaxChannel(ReadPixels(harness.System, outDbgVis, size)), Is.GreaterThan(100));
        Assert.That(MaxChannel(ReadPixels(harness.System, outDbgRaw, size)), Is.GreaterThan(20));

        // 5. Verify opt-in half-resolution tracing and runtime toggling back to full resolution.
        scene.LightingDebugView = World3DLightingDebugView.None;
        scene.VoxelGi.HalfResolutionTrace = true;
        for (int i = 0; i < 3; ++i)
            harness.SubmitFrame(outHalfRes, cam);
        double avgFloorHalf = AverageRegionLuma(ReadPixels(harness.System, outHalfRes, size), size, size / 2 - 12, size / 2 + 12, (int)(size * 0.72), (int)(size * 0.85));
        Assert.That(avgFloorHalf, Is.GreaterThan(avgFloorOff + 18.0), "Opt-in half-res VoxelGI must still illuminate shaded Cornell floor.");

        scene.VoxelGi.HalfResolutionTrace = false;
        harness.SubmitFrame(outGiOn, cam);
        double avgFloorFullBack = AverageRegionLuma(ReadPixels(harness.System, outGiOn, size), size, size / 2 - 12, size / 2 + 12, (int)(size * 0.72), (int)(size * 0.85));
        Assert.That(avgFloorFullBack, Is.GreaterThan(avgFloorOff + 18.0), "Toggling back to full-res VoxelGI must continue illuminating shaded Cornell floor.");
    }

    /// <summary>Checks that a sub-voxel emitter does not get brighter when sampled from a coarser cascade.</summary>
    [Test]
    public void VulkanGpuSmallEmissive_DoesNotBrightenOnCoarserCascade()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        const int size = 160;
        using Texture output = harness.CreateOutput(size, size);

        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = false;
        scene.Lighting = scene.Lighting with { SunIntensity = 0f, SkyExposure = 0f, AmbientFloor = 0f, NightFloor = 0f };
        scene.LightingDebugView = World3DLightingDebugView.GiDiffuse;
        scene.VoxelGi.Resolution = 64;
        scene.VoxelGi.StaticBrickPoolCapacity = 512;
        scene.VoxelGi.DynamicBrickPoolCapacity = 128;
        scene.VoxelGi.BootstrapFullVolumeOnReset = true;

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle geometry), Is.True);
        Assert.That(scene.TryQueueGeometryLod(geometry, 0, s_boxVertices, s_boxIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = Vector4.One }, out WorldMaterialHandle wallMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { Emissive = new Vector3(6f, 0f, 0f) }, out WorldMaterialHandle lightMat), Is.True);
        AddWall(scene, sector, geometry, wallMat, Vector3.Zero, 0.125f, 0f, 2f, 0.125f, 1.5f, 1.5f);
        AddWall(scene, sector, geometry, lightMat, Vector3.Zero, -1.7f, 0.38f, 2.14f, 0.035f, 0.055f, 0.055f);

        World3DCamera near = LookAt(new Vector3(-4f, 0f, 2f), new Vector3(0f, 0f, 2f));
        // Match the wall footprint at three times the viewing distance, outside cascade 0's 8 m half-extent.
        World3DCamera far = near with
        {
            Eye = new World3DPosition(-12f, 0f, 2f),
            Fov = 2f * MathF.Atan(MathF.Tan(near.Fov * 0.5f) / 3f),
        };

        double nearTint = MeasureWallTint(near);
        double farTint = MeasureWallTint(far);
        Assert.That(Math.Min(nearTint, farTint), Is.GreaterThan(3.0), "The wall must receive emissive GI in both cascades.");
        Assert.That(farTint, Is.EqualTo(nearTint).Within(5.0), $"Wall tint must stay consistent across cascades: near={nearTint:F1}, far={farTint:F1}.");

        double MeasureWallTint(World3DCamera camera)
        {
            const int min = size / 2 - 20;
            const int max = size / 2 + 20;
            const int samples = 4;
            double tint = 0.0;
            for (int i = 0; i < 8; ++i)
                harness.SubmitFrame(output, camera);
            for (int i = 0; i < samples; ++i)
            {
                harness.SubmitFrame(output, camera);
                (double red, double green, _) = AverageRegionRgb(ReadPixels(harness.System, output, size), size, min, max, min, max);
                tint += red - green;
            }
            return tint / samples;
        }
    }

    /// <summary>Verifies custom IVoxelFeedSurface voxelization and dynamic instance GI movement with zero residual ghosts.</summary>
    [Test]
    public void VulkanGpuCustomSurfaceAndDynamicInstances_FeedAndTrackMovesCleanly()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        const int size = 160;
        using Texture outLeft = harness.CreateOutput(size, size);
        using Texture outRight = harness.CreateOutput(size, size);

        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = false;
        scene.Lighting = scene.Lighting with { SunIntensity = 0f, SkyExposure = 0f, AmbientFloor = 0f };
        scene.VoxelGi.Resolution = 64;
        scene.VoxelGi.BaseVoxelSize = 0.25f;
        scene.VoxelGi.StaticBrickPoolCapacity = 2048;
        scene.VoxelGi.DynamicBrickPoolCapacity = 512;
        scene.VoxelGi.BootstrapFullVolumeOnReset = true;
        scene.VoxelGi.IndirectDiffuseIntensity = 2.0f;
        scene.VoxelGi.TemporalBlendSpeed = 20f;

        harness.System.Shaders.Compiler.RegisterVirtualModule(CustomSurfaceModule, CustomSurfaceModule + ".slang", CustomSurfaceSource);

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle boxGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(boxGeom, 0, s_boxVertices, s_boxIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = Vector4.One, Roughness = 0.8f }, out WorldMaterialHandle whiteMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { SurfaceModule = CustomSurfaceModule }, out WorldMaterialHandle customMat), Is.True);

        // White receiving floor.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, boxGeom, whiteMat,
            new World3DTransform(new World3DPosition(0, 0, -0.5), Quaternion.Identity, new Vector3(5f, 5f, 0.5f))), out _), Is.True);

        // Dynamic custom-surface cyan emissive crate initially on left (-Y).
        World3DInstanceDesc dynLeft = new(sector, boxGeom, customMat,
            new World3DTransform(new World3DPosition(0, -2.5, 0.9), Quaternion.Identity, new Vector3(0.75f, 0.75f, 0.75f)),
            Dynamic: true);
        Assert.That(scene.TryAddInstance(in dynLeft, out WorldInstanceHandle dynHandle), Is.True);

        World3DCamera cam = LookAt(new Vector3(-7f, 0f, 3.5f), new Vector3(0f, 0f, 0.2f));
        for (int i = 0; i < 4; ++i)
            harness.SubmitFrame(outLeft, cam);

        Assert.That(scene.TryReadVoxelGiCounters(out World3DVoxelGiGpuCounters dynCounters), Is.True);
        Assert.That(dynCounters.DynamicUsedBricks, Is.GreaterThan(0u));
        byte[] pxLeft = ReadPixels(harness.System, outLeft, size);

        // Move dynamic crate to right (+Y) and render enough frames for temporal settle.
        World3DInstanceDesc dynRight = dynLeft with
        {
            Transform = new World3DTransform(new World3DPosition(0, 2.5, 0.9), Quaternion.Identity, new Vector3(0.75f, 0.75f, 0.75f)),
        };
        Assert.That(scene.TryUpdateInstance(dynHandle, in dynRight), Is.True);
        for (int i = 0; i < 6; ++i)
            harness.SubmitFrame(outRight, cam);

        byte[] pxRight = ReadPixels(harness.System, outRight, size);
        double lFloorLeftFrame = AverageRegionLuma(pxLeft, size, (int)(size * 0.22), (int)(size * 0.36), (int)(size * 0.58), (int)(size * 0.72));
        double rFloorLeftFrame = AverageRegionLuma(pxLeft, size, (int)(size * 0.64), (int)(size * 0.78), (int)(size * 0.58), (int)(size * 0.72));
        double lFloorRightFrame = AverageRegionLuma(pxRight, size, (int)(size * 0.22), (int)(size * 0.36), (int)(size * 0.58), (int)(size * 0.72));
        double rFloorRightFrame = AverageRegionLuma(pxRight, size, (int)(size * 0.64), (int)(size * 0.78), (int)(size * 0.58), (int)(size * 0.72));

        Assert.That(lFloorLeftFrame, Is.GreaterThan(rFloorLeftFrame + 10.0), "Left floor receives cyan GI when dynamic emitter is on the left.");
        Assert.That(rFloorRightFrame, Is.GreaterThan(lFloorRightFrame + 10.0), "Right floor receives cyan GI after dynamic emitter moves right with no ghost on the left.");
    }

    /// <summary>Verifies RSM sun bounce boost and World3DPointLight direct and indirect lighting.</summary>
    [Test]
    public void VulkanGpuRsmAndPointLights_EnhanceBounceAndLightInterior()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        const int size = 160;
        using Texture outNoRsm = harness.CreateOutput(size, size);
        using Texture outWithRsm = harness.CreateOutput(size, size);
        using Texture outNoPoint = harness.CreateOutput(size, size);
        using Texture outWithPoint = harness.CreateOutput(size, size);

        World3DScene scene = harness.Scene;
        scene.VoxelGi.Resolution = 64;
        scene.VoxelGi.BaseVoxelSize = 0.25f;
        scene.VoxelGi.StaticBrickPoolCapacity = 2048;
        scene.VoxelGi.DynamicBrickPoolCapacity = 512;
        scene.VoxelGi.BootstrapFullVolumeOnReset = true;
        scene.Shadows.Enabled = true;
        scene.Shadows.CascadeResolution = 512;

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle boxGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(boxGeom, 0, s_boxVertices, s_boxIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.9f, 0.9f, 0.9f, 1f) }, out WorldMaterialHandle whiteMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.95f, 0.1f, 0.05f, 1f) }, out WorldMaterialHandle redMat), Is.True);

        // Floor + sunlit red reflector wall + tall blocker creating a shaded nook in front of the red wall.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, boxGeom, whiteMat,
            new World3DTransform(new World3DPosition(0, 0, -0.5), Quaternion.Identity, new Vector3(12f, 12f, 0.5f))), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, boxGeom, redMat,
            new World3DTransform(new World3DPosition(2.0, 0, 2.0), Quaternion.Identity, new Vector3(0.4f, 4f, 2f))), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, boxGeom, whiteMat,
            new World3DTransform(new World3DPosition(-1.5, 0, 2.0), Quaternion.Identity, new Vector3(0.4f, 3f, 2f))), out _), Is.True);

        // Sun shines from -X/+Z onto +X red wall, shadowing the white floor gap behind the -X blocker.
        scene.Lighting = scene.Lighting with
        {
            DirectionToSun = Vector3.Normalize(new Vector3(-0.7f, 0.1f, 0.7f)),
            SunIntensity = 12f,
            AmbientFloor = 0f,
            SkyExposure = 0.15f,
        };
        World3DCamera cam = LookAt(new Vector3(0.2f, -4.2f, 1.4f), new Vector3(0.4f, 0.2f, 0.5f));

        scene.Shadows.Rsm.Enabled = false;
        for (int i = 0; i < 3; ++i)
            harness.SubmitFrame(outNoRsm, cam);
        byte[] noRsmPx = ReadPixels(harness.System, outNoRsm, size);

        scene.Shadows.Rsm.Enabled = true;
        scene.Shadows.Rsm.Resolution = 256;
        for (int i = 0; i < 3; ++i)
            harness.SubmitFrame(outWithRsm, cam);
        byte[] rsmPx = ReadPixels(harness.System, outWithRsm, size);

        (double noRsmR, _, _) = AverageRegionRgb(noRsmPx, size, (int)(size * 0.35), (int)(size * 0.65), (int)(size * 0.62), (int)(size * 0.78));
        (double rsmR, _, _) = AverageRegionRgb(rsmPx, size, (int)(size * 0.35), (int)(size * 0.65), (int)(size * 0.62), (int)(size * 0.78));
        Assert.That(rsmR, Is.GreaterThan(noRsmR + 4.0), "RSM near-field bounce must boost red reflected radiance in shaded crevice.");

        // Point light dark-room check: turns dark room bright on both floor and indirect wall bounce.
        scene.Shadows.Enabled = false;
        scene.Lighting = scene.Lighting with { SunIntensity = 0f, SkyExposure = 0f, AmbientFloor = 0f };
        scene.SetPointLights([]);
        for (int i = 0; i < 3; ++i)
            harness.SubmitFrame(outNoPoint, cam);
        double darkLuma = AverageRegionLuma(ReadPixels(harness.System, outNoPoint, size), size, 20, size - 20, 20, size - 20);

        scene.SetPointLights([new World3DPointLight(new World3DPosition(0.2, 0.0, 1.6), new Vector3(1.0f, 0.85f, 0.5f), 25f, 8f)]);
        for (int i = 0; i < 3; ++i)
            harness.SubmitFrame(outWithPoint, cam);
        double litLuma = AverageRegionLuma(ReadPixels(harness.System, outWithPoint, size), size, 20, size - 20, 20, size - 20);
        Assert.That(litLuma, Is.GreaterThan(darkLuma + 25.0), "Point light must illuminate scene directly and through GI bounce.");
    }

    /// <summary>Verifies identical VoxelGI radiance output at origin and 500 km world coordinates.</summary>
    [Test]
    public void VulkanGpu500KmConsistency_MatchesOriginCornellGi()
    {
        using SceneHarness hOrigin = new(GraphicsBackend.VulkanCompact);
        using SceneHarness hFar = new(GraphicsBackend.VulkanCompact);
        const int size = 128;
        using Texture outOrigin = hOrigin.CreateOutput(size, size);
        using Texture outFar = hFar.CreateOutput(size, size);

        Vector3 offset = new(500_000f, -320_000f, 120f);
        SetupScene(hOrigin.Scene, Vector3.Zero);
        SetupScene(hFar.Scene, offset);

        World3DCamera camOrigin = LookAt(new Vector3(-2.6f, 0f, 2.2f), new Vector3(2.0f, 0f, 1.8f));
        World3DCamera camFar = new(new World3DPosition(offset.X - 2.6, offset.Y, offset.Z + 2.2), camOrigin.Rotation, camOrigin.Fov, camOrigin.Near);

        for (int i = 0; i < 3; ++i)
        {
            hOrigin.SubmitFrame(outOrigin, camOrigin);
            hFar.SubmitFrame(outFar, camFar);
        }

        byte[] pxOrigin = ReadPixels(hOrigin.System, outOrigin, size);
        byte[] pxFar = ReadPixels(hFar.System, outFar, size);

        long diffSum = 0;
        int maxDiff = 0;
        for (int i = 0; i < pxOrigin.Length; i += 4)
        {
            for (int ch = 0; ch < 3; ++ch)
            {
                int d = Math.Abs(pxOrigin[i + ch] - pxFar[i + ch]);
                diffSum += d;
                if (d > maxDiff) maxDiff = d;
            }
        }
        double meanDiff = (double)diffSum / (size * size * 3);
        Assert.That(meanDiff, Is.LessThan(2.5), $"500 km GI mean pixel delta was {meanDiff:F2}.");
        Assert.That(maxDiff, Is.LessThanOrEqualTo(18), $"500 km GI max pixel delta was {maxDiff}.");

        static void SetupScene(World3DScene s, Vector3 o)
        {
            s.Shadows.Enabled = false;
            s.Lighting = s.Lighting with { SunIntensity = 0f, SkyExposure = 0f, AmbientFloor = 0f };
            s.VoxelGi.Resolution = 64;
            s.VoxelGi.BaseVoxelSize = 0.25f;
            s.VoxelGi.StaticBrickPoolCapacity = 2048;
            s.VoxelGi.DynamicBrickPoolCapacity = 512;
            s.VoxelGi.BootstrapFullVolumeOnReset = true;
            s.VoxelGi.IndirectDiffuseIntensity = 1.5f;
            BuildCornellScene(s, o, includeInteriorCrate: false);
        }
    }

    /// <summary>Stresses rapid camera flight through many static instances ensuring O(1) CPU prepare times and zero pool exhaustion.</summary>
    [Test]
    public void VulkanGpuFastCameraFly_HasFlatCpuRecordTimeWithoutPoolExhaustion()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact, textureHeapMb: 96);
        using Texture output = harness.CreateOutput(256, 256);
        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = false;
        scene.VoxelGi.Resolution = 64;
        scene.VoxelGi.BaseVoxelSize = 0.5f;
        scene.VoxelGi.StaticBrickPoolCapacity = 2048;
        scene.VoxelGi.DynamicBrickPoolCapacity = 256;
        scene.VoxelGi.StaticBrickBudgets = [512, 256, 128, 128];
        scene.VoxelGi.BootstrapFullVolumeOnReset = false;

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle geom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(geom, 0, s_boxVertices, s_boxIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.75f, 0.72f, 0.68f, 1f) }, out WorldMaterialHandle mat), Is.True);

        for (int i = 0; i < 300; ++i)
        {
            double x = (i % 50) * 4.0;
            double y = ((i / 50) - 2.5) * 6.0;
            Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, mat, TransformAt(x, y, 1.0, 1.0f)), out _), Is.True);
        }

        // Frame 0 uploads geometry.
        harness.SubmitFrame(output, LookAt(new Vector3(-5f, 0f, 3f), new Vector3(10f, 0f, 1f)));

        List<double> msSamples = [];
        for (int f = 0; f < 30; ++f)
        {
            World3DCamera cam = LookAt(new Vector3(f * 3.5f, 0f, 4f), new Vector3(f * 3.5f + 12f, 0f, 1f));
            long t0 = Stopwatch.GetTimestamp();
            harness.SubmitFrame(output, cam);
            double elapsedMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (f >= 2)
                msSamples.Add(elapsedMs);
        }

        Assert.That(scene.TryReadVoxelGiCounters(out World3DVoxelGiGpuCounters counters), Is.True);
        Assert.That(counters.StaticFallbackBricks, Is.EqualTo(0u));

        msSamples.Sort();
        double median = msSamples[msSamples.Count / 2];
        double max = msSamples[^1];
        Assert.That(max, Is.LessThan(Math.Max(median * 4.0, median + 15.0)),
            $"Fast camera fly exhibited spike: median={median:F2}ms, max={max:F2}ms.");
    }

    /// <summary>Verifies open walls in coarser cascades (Cascades 1 and 2 beyond Cascade 0) do not self-occlude.</summary>
    [Test]
    public void VulkanGpuDistantCascades_DoNotSelfOccludeBeyondCascade0()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        const int size = 192;
        using Texture outVis = harness.CreateOutput(size, size);
        using Texture outColor = harness.CreateOutput(size, size);

        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = false;
        scene.Lighting = scene.Lighting with { SunIntensity = 0f, SkyExposure = 1.0f, AmbientFloor = 0.25f };
        scene.VoxelGi.Resolution = 64;
        scene.VoxelGi.BaseVoxelSize = 0.25f;
        scene.VoxelGi.StaticBrickPoolCapacity = 2048;
        scene.VoxelGi.DynamicBrickPoolCapacity = 256;
        scene.VoxelGi.BootstrapFullVolumeOnReset = true;

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle boxGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(boxGeom, 0, s_boxVertices, s_boxIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.85f, 0.85f, 0.85f, 1f), Roughness = 0.85f }, out WorldMaterialHandle whiteMat), Is.True);

        // Open walls placed in Cascade 0 (<8m), Cascade 1 (8..16m), and Cascade 2 (16..32m).
        AddWall(scene, sector, boxGeom, whiteMat, Vector3.Zero, 5.5f, -1.7f, 2.0f, 0.35f, 0.8f, 2.0f);
        AddWall(scene, sector, boxGeom, whiteMat, Vector3.Zero, 12.0f, 0.0f, 2.0f, 0.35f, 1.6f, 3.0f);
        AddWall(scene, sector, boxGeom, whiteMat, Vector3.Zero, 22.0f, 7.4f, 2.0f, 0.35f, 3.2f, 4.5f);

        World3DCamera cam = LookAt(new Vector3(0f, 0f, 2.0f), new Vector3(10f, 0f, 2.0f));
        scene.LightingDebugView = World3DLightingDebugView.GiVisibility;
        for (int i = 0; i < 4; ++i)
            harness.SubmitFrame(outVis, cam);
        byte[] visPx = ReadPixels(harness.System, outVis, size);

        double c0Vis = AverageRegionLuma(visPx, size, (int)(size * 0.10), (int)(size * 0.22), (int)(size * 0.38), (int)(size * 0.62));
        double c1Vis = AverageRegionLuma(visPx, size, (int)(size * 0.44), (int)(size * 0.56), (int)(size * 0.38), (int)(size * 0.62));
        double c2Vis = AverageRegionLuma(visPx, size, (int)(size * 0.74), (int)(size * 0.88), (int)(size * 0.38), (int)(size * 0.62));
        Assert.That(c0Vis, Is.GreaterThan(120.0), "Cascade 0 open wall must have high sky visibility.");
        Assert.That(c1Vis, Is.GreaterThan(Math.Max(120.0, c0Vis * 0.70)), "Cascade 1 wall must not self-occlude.");
        Assert.That(c2Vis, Is.GreaterThan(Math.Max(120.0, c0Vis * 0.70)), "Cascade 2 wall must not self-occlude.");

        scene.LightingDebugView = World3DLightingDebugView.None;
        for (int i = 0; i < 2; ++i)
            harness.SubmitFrame(outColor, cam);
        byte[] colorPx = ReadPixels(harness.System, outColor, size);

        double c0Luma = AverageRegionLuma(colorPx, size, (int)(size * 0.10), (int)(size * 0.22), (int)(size * 0.38), (int)(size * 0.62));
        double c1Luma = AverageRegionLuma(colorPx, size, (int)(size * 0.44), (int)(size * 0.56), (int)(size * 0.38), (int)(size * 0.62));
        double c2Luma = AverageRegionLuma(colorPx, size, (int)(size * 0.74), (int)(size * 0.88), (int)(size * 0.38), (int)(size * 0.62));
        Assert.That(c1Luma, Is.GreaterThan(Math.Max(45.0, c0Luma * 0.65)), "Cascade 1 lit wall must remain illuminated.");
        Assert.That(c2Luma, Is.GreaterThan(Math.Max(45.0, c0Luma * 0.65)), "Cascade 2 lit wall must remain illuminated.");
    }

    private static void BuildCornellScene(World3DScene scene, Vector3 offset, bool includeInteriorCrate)
    {
        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle geom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(geom, 0, s_boxVertices, s_boxIndices), Is.True);

        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.85f, 0.85f, 0.85f, 1f), Roughness = 0.85f }, out WorldMaterialHandle whiteMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.9f, 0.08f, 0.06f, 1f), Roughness = 0.85f }, out WorldMaterialHandle redMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.06f, 0.9f, 0.08f, 1f), Roughness = 0.85f }, out WorldMaterialHandle greenMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = Vector4.One,
            Emissive = new Vector3(5.5f, 5.5f, 5.5f),
            Roughness = 0.9f,
        }, out WorldMaterialHandle lightMat), Is.True);

        // Floor, ceiling, back wall (+X), front wall (-X), left green (-Y), right red (+Y), emissive ceiling panel.
        AddWall(scene, sector, geom, whiteMat, offset, 0f, 0f, -0.25f, 3.2f, 3.2f, 0.25f);
        AddWall(scene, sector, geom, whiteMat, offset, 0f, 0f, 4.25f, 3.2f, 3.2f, 0.25f);
        AddWall(scene, sector, geom, whiteMat, offset, 3.25f, 0f, 2.0f, 0.25f, 3.2f, 2.2f);
        AddWall(scene, sector, geom, whiteMat, offset, -3.25f, 0f, 2.0f, 0.25f, 3.2f, 2.2f);
        AddWall(scene, sector, geom, greenMat, offset, 0f, -3.0f, 2.0f, 3.2f, 0.25f, 2.2f);
        AddWall(scene, sector, geom, redMat, offset, 0f, 3.0f, 2.0f, 3.2f, 0.25f, 2.2f);
        AddWall(scene, sector, geom, lightMat, offset, 0.2f, 0f, 3.9f, 1.2f, 1.2f, 0.15f);

        if (includeInteriorCrate)
        {
            Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
            {
                BaseColor = new Vector4(0.85f, 0.85f, 0.85f, 1f),
                Metallic = 0.8f,
                Roughness = 0.2f,
            }, out WorldMaterialHandle glossMat), Is.True);
            AddWall(scene, sector, geom, glossMat, offset, 0.8f, 0.6f, 0.6f, 0.6f, 0.6f, 0.6f);
        }
    }

    private static void AddWall(World3DScene scene, WorldSectorHandle sector, WorldGeometryHandle geom, WorldMaterialHandle mat,
        Vector3 offset, float x, float y, float z, float sx, float sy, float sz)
    {
        World3DTransform tf = new(new World3DPosition(offset.X + x, offset.Y + y, offset.Z + z), Quaternion.Identity, new Vector3(sx, sy, sz));
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, mat, tf), out _), Is.True);
    }

    private static World3DTransform TransformAt(double x, double y, double z, float scale = 1f) =>
        new(new World3DPosition(x, y, z), Quaternion.Identity, new Vector3(scale));

    private static World3DCamera LookAt(Vector3 eye, Vector3 target)
    {
        Vector3 f = Vector3.Normalize(target - eye);
        Vector3 up = MathF.Abs(f.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitY;
        Vector3 r = Vector3.Normalize(Vector3.Cross(up, f));
        Vector3 u = Vector3.Cross(f, r);
        Quaternion rot = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            f.X, f.Y, f.Z, 0f, r.X, r.Y, r.Z, 0f, u.X, u.Y, u.Z, 0f, 0f, 0f, 0f, 1f));
        return new World3DCamera(new World3DPosition(eye.X, eye.Y, eye.Z), rot, 0.9f, 0.1f);
    }

    private static byte[] ReadPixels(RenderingSystem system, Texture tex, int size)
    {
        byte[] bytes = new byte[size * size * 4];
        system.ReadTexture(tex, bytes);
        return bytes;
    }

    private static byte MaxChannel(byte[] pixels)
    {
        byte m = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            m = Math.Max(m, Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])));
        return m;
    }

    private static (double R, double G, double B) AverageRegionRgb(byte[] px, int size, int x0, int x1, int y0, int y1)
    {
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = y0; y < y1; ++y)
        {
            for (int x = x0; x < x1; ++x)
            {
                int idx = (y * size + x) * 4;
                r += px[idx];
                g += px[idx + 1];
                b += px[idx + 2];
                ++n;
            }
        }
        double inv = n > 0 ? 1.0 / n : 0.0;
        return (r * inv, g * inv, b * inv);
    }

    private static double AverageRegionLuma(byte[] px, int size, int x0, int x1, int y0, int y1)
    {
        (double r, double g, double b) = AverageRegionRgb(px, size, x0, x1, y0, y1);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static VertexPBR[] CreateBoxVertices() =>
    [
        // -Z face
        new(new Vector3(-1, -1, -1), -Vector3.UnitZ, Vector2.Zero),
        new(new Vector3(-1, 1, -1), -Vector3.UnitZ, Vector2.UnitY),
        new(new Vector3(1, 1, -1), -Vector3.UnitZ, Vector2.One),
        new(new Vector3(1, -1, -1), -Vector3.UnitZ, Vector2.UnitX),
        // +Z face
        new(new Vector3(-1, -1, 1), Vector3.UnitZ, Vector2.Zero),
        new(new Vector3(1, -1, 1), Vector3.UnitZ, Vector2.UnitX),
        new(new Vector3(1, 1, 1), Vector3.UnitZ, Vector2.One),
        new(new Vector3(-1, 1, 1), Vector3.UnitZ, Vector2.UnitY),
        // +X face
        new(new Vector3(1, -1, -1), Vector3.UnitX, Vector2.Zero),
        new(new Vector3(1, 1, -1), Vector3.UnitX, Vector2.UnitX),
        new(new Vector3(1, 1, 1), Vector3.UnitX, Vector2.One),
        new(new Vector3(1, -1, 1), Vector3.UnitX, Vector2.UnitY),
        // -X face
        new(new Vector3(-1, -1, -1), -Vector3.UnitX, Vector2.Zero),
        new(new Vector3(-1, -1, 1), -Vector3.UnitX, Vector2.UnitY),
        new(new Vector3(-1, 1, 1), -Vector3.UnitX, Vector2.One),
        new(new Vector3(-1, 1, -1), -Vector3.UnitX, Vector2.UnitX),
        // +Y face
        new(new Vector3(1, 1, -1), Vector3.UnitY, Vector2.Zero),
        new(new Vector3(-1, 1, -1), Vector3.UnitY, Vector2.UnitX),
        new(new Vector3(-1, 1, 1), Vector3.UnitY, Vector2.One),
        new(new Vector3(1, 1, 1), Vector3.UnitY, Vector2.UnitY),
        // -Y face
        new(new Vector3(-1, -1, -1), -Vector3.UnitY, Vector2.Zero),
        new(new Vector3(1, -1, -1), -Vector3.UnitY, Vector2.UnitX),
        new(new Vector3(1, -1, 1), -Vector3.UnitY, Vector2.One),
        new(new Vector3(-1, -1, 1), -Vector3.UnitY, Vector2.UnitY),
    ];

    private static uint[] CreateBoxIndices() =>
    [
        0, 1, 2, 0, 2, 3,
        4, 5, 6, 4, 6, 7,
        8, 9, 10, 8, 10, 11,
        12, 13, 14, 12, 14, 15,
        16, 17, 18, 16, 18, 19,
        20, 21, 22, 20, 22, 23,
    ];

    private sealed class SceneHarness : IDisposable
    {
        private readonly GPUDevice _device;

        public SceneHarness(GraphicsBackend backend, int textureHeapMb = 128)
        {
            _device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true });
            RenderingSystem? system = null;
            RenderContext? context = null;
            try
            {
                ulong texHeap = backend == GraphicsBackend.NoGPU ? 2ul * 1024 * 1024 : (ulong)textureHeapMb * 1024 * 1024;
                ulong bufHeap = backend == GraphicsBackend.NoGPU ? 2ul * 1024 * 1024 : 64ul * 1024 * 1024;
                System = system = new RenderingSystem(_device, textureHeapByteCount: texHeap,
                    bufferHeapByteCount: bufHeap, uploadStagingByteCount: 512 * 1024);
                Context = context = new RenderContext(System, frameDataByteCount: 2 * 1024 * 1024);
                Scene = new World3DScene(System);
                Scene.Forward.Enabled = false;
                Scene.VolumetricClouds.Enabled = false;
                Scene.VolumetricLight.Enabled = false;
                Scene.Ssr.Enabled = false;
                Scene.AutoExposure.Enabled = false;
            }
            catch
            {
                context?.Dispose();
                system?.Dispose();
                _device.Destroy();
                throw;
            }
        }

        public RenderingSystem System { get; }
        public RenderContext Context { get; }
        public World3DScene Scene { get; }

        public Texture CreateOutput(int width, int height) => System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D((uint)width, (uint)height, 1),
            Format = Format.Rgba8Srgb,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });

        public World3DView RecordFrame(Texture output, World3DCamera camera)
        {
            Context.BeginFrame();
            RenderGraphTarget target = RenderGraphTarget.FromTexture(output);
            Assert.That(Scene.TryRecordFrame(Context, camera, new Extent2D(output.Extent.X, output.Extent.Y),
                out World3DView view, target, RGUsage.TransferRead, renderWorld: true, deltaTime: 1f / 30f), Is.True);
            return view;
        }

        public World3DView SubmitFrame(Texture output, World3DCamera camera)
        {
            World3DView view = RecordFrame(output, camera);
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
