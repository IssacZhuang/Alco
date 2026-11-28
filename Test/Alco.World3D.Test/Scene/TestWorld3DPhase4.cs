using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>Exercises Phase 4 rendering features: GPU forward sort/glass refraction, volumetric clouds + shadows, volumetric light, and Hi-Z SSR.</summary>
[TestFixture]
public sealed class TestWorld3DPhase4
{
    private const string CustomSurfaceModule = "TestPhase4CustomGlassSurface";
    private const string CustomSurfaceSource = """
        #language slang 2025
        module TestPhase4CustomGlassSurface;
        import World3DSurface;

        public struct TestPhase4CustomGlassSurface : ISurface
        {
            public override float4 getBaseColor(SurfaceInput input)
            {
                return float4(0.15, 0.90, 0.85, 0.45);
            }

            public override float3 getEmissive(SurfaceInput input)
            {
                return float3(0.35, 3.2, 2.8);
            }
        };
        """;

    private static readonly Vector3 s_boxMin = new(-1f, -1f, -1f);
    private static readonly Vector3 s_boxMax = new(1f, 1f, 1f);
    private static readonly VertexPBR[] s_boxVertices = CreateBoxVertices();
    private static readonly uint[] s_boxIndices = CreateBoxIndices();

    /// <summary>Compiles all Phase 4 compute/graphics shaders and composed forward ISurface variants across every backend target.</summary>
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Dxil)]
    [TestCase(ShaderTarget.MetalSource)]
    public void ValidateShader_CompilesPhase4ModulesAndComposedForward(ShaderTarget target)
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
            "World3DAtmosphere",
            "World3DCloudCommon",
            "World3DCloudNoise",
            "World3DCloudShadow",
            "World3DCloudMarch",
            "World3DCloudTemporal",
            "World3DCloudComposite",
            "World3DVolumetricLightMarch",
            "World3DVolumetricLightComposite",
            "World3DForwardCullKey",
            "World3DForwardSort",
            "World3DForwardEmitArgs",
            "World3DForwardSnapshot",
            "World3DForward",
            "World3DSsrBlueNoise",
            "World3DSsrColorMip",
            "World3DSsrTrace",
            "World3DSsrResolve",
            "World3DSsrComposite",
        ];
        foreach (string module in modules)
        {
            string file = module + ".slang";
            using StreamReader reader = new(typeof(World3DScene).Assembly.GetManifestResourceStream(file)
                ?? throw new InvalidOperationException($"Missing embedded shader {file}."));
            compiler.RegisterVirtualModule(module, file, reader.ReadToEnd());
        }
        compiler.RegisterVirtualModule(CustomSurfaceModule, CustomSurfaceModule + ".slang", CustomSurfaceSource);

        IReadOnlyList<ShaderTypeLayout> shadowTypes = compiler.LoadModuleFromSource(
            "World3DShadowSample", "World3DShadowSample.slang",
            new StreamReader(typeof(World3DScene).Assembly.GetManifestResourceStream("World3DShadowSample.slang")!).ReadToEnd()).GetTypes();
        Assert.That(shadowTypes.First(t => t.TypeName == "World3DShadowFrameGpu").SizeBytes, Is.EqualTo(672u));

        IReadOnlyList<ShaderTypeLayout> cloudTypes = compiler.LoadModuleFromSource(
            "World3DCloudCommon", "World3DCloudCommon.slang",
            new StreamReader(typeof(World3DScene).Assembly.GetManifestResourceStream("World3DCloudCommon.slang")!).ReadToEnd()).GetTypes();
        Assert.That(cloudTypes.First(t => t.TypeName == "World3DCloudFrameGpu").SizeBytes, Is.EqualTo(176u));

        ReadOnlySpan<(string Module, uint RootBytes)> kernels =
        [
            ("World3DCloudNoise", 20),
            ("World3DCloudShadow", 36),
            ("World3DCloudMarch", 40),
            ("World3DCloudTemporal", 48),
            ("World3DCloudComposite", 48),
            ("World3DVolumetricLightMarch", 112),
            ("World3DVolumetricLightComposite", 40),
            ("World3DForwardCullKey", 64),
            ("World3DForwardSort", 32),
            ("World3DForwardEmitArgs", 40),
            ("World3DForwardSnapshot", 24),
            ("World3DSsrBlueNoise", 20),
            ("World3DSsrColorMip", 32),
            ("World3DSsrTrace", 96),
            ("World3DSsrResolve", 52),
            ("World3DSsrComposite", 80),
        ];
        foreach ((string module, uint rootBytes) in kernels)
            Assert.That(compiler.Compile(module, ShaderPipelineKind.Compute).Reflection.Root?.SizeBytes, Is.EqualTo(rootBytes), module);

        ReadOnlySpan<string> surfaces = ["World3DPbrStandard", CustomSurfaceModule];
        foreach (string surface in surfaces)
        {
            CompiledShader composed = compiler.CompileComposed("World3DForward", surface, ShaderPipelineKind.Graphics);
            Assert.That(composed.Reflection.Root?.SizeBytes, Is.EqualTo(128u), $"Forward+{surface}");
        }
    }

    /// <summary>Verifies material Transmission/IOR validation, Blend routing isolation, and 500 km cloud phase determinism on NoGPU.</summary>
    [Test]
    public void NoGpu_MaterialValidation_ForwardRouting_And_500KmCloudPhaseWrap()
    {
        using SceneHarness h = new(GraphicsBackend.NoGPU, textureHeapMb: 8);
        World3DScene scene = h.Scene;

        Assert.That(scene.VolumetricClouds.Enabled, Is.True);
        Assert.That(scene.VolumetricLight.Enabled, Is.True);
        Assert.That(scene.Ssr.Enabled, Is.True);
        Assert.That(scene.Forward.Enabled, Is.True);

        // Material validation for Transmission and IOR.
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { Transmission = -0.1f }, out _), Is.False);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { Transmission = 1.1f }, out _), Is.False);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { Ior = 0.8f }, out _), Is.False);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { Ior = 3.2f }, out _), Is.False);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { Ior = float.NaN }, out _), Is.False);

        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.8f, 0.9f, 1.0f, 0.25f),
            AlphaMode = World3DAlphaMode.Blend,
            Transmission = 0.9f,
            Ior = 1.52f,
        }, out WorldMaterialHandle glassMat), Is.True);

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        WorldGeometryHandle geom = UploadBoxGeometry(h.System, scene);

        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, glassMat, TransformAt(6, 0, 0)), out WorldInstanceHandle inst), Is.True);

        using Texture output = h.CreateOutput(64, 64);
        World3DCamera farCam = LookAt(new Vector3(500_000f, -320_000f, 120f), new Vector3(500_010f, -320_000f, 120f));
        Assert.That(scene.TryUpdateInstance(inst, new World3DInstanceDesc(sector, geom, glassMat, TransformAt(500_006, -320_000, 120))), Is.True);

        h.SubmitFrame(output, farCam);

        // Blend instance must NOT generate opaque indirect draws.
        Assert.That(scene.LastRecordedDraws.IndirectSlots, Is.EqualTo(0));
        Assert.That(scene.LastRecordedForward.Enabled, Is.True);
        Assert.That(scene.LastRecordedForward.CandidateCount, Is.EqualTo(1u));
        Assert.That(scene.LastRecordedForward.BucketCount, Is.EqualTo(1));
        Assert.That(scene.LastRecordedVolumetricClouds.Enabled, Is.True);
        Assert.That(scene.LastRecordedVolumetricLight.Enabled, Is.True);
        Assert.That(scene.LastRecordedSsr.Enabled, Is.True);
    }

    /// <summary>Verifies GPU bitonic sorting across >256 transparent candidates, back-to-front ordering, Hi-Z culling, custom ISurface, and depth-safe refraction.</summary>
    [Test]
    public void VulkanCompact_Forward_SortOver256_HiZCull_CustomSurface_And_SafeRefraction()
    {
        using SceneHarness h = new(GraphicsBackend.VulkanCompact, textureHeapMb: 128);
        World3DScene scene = h.Scene;
        scene.Shadows.Enabled = false;
        scene.VoxelGi.Enabled = false;
        scene.VolumetricClouds.Enabled = false;
        scene.VolumetricLight.Enabled = false;
        scene.Ssr.Enabled = false;
        scene.Occlusion.Enabled = false;
        scene.AutoExposure.Enabled = false;

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        WorldGeometryHandle geom = UploadBoxGeometry(h.System, scene);

        // 1. Multi-tile (>256) bitonic sort order test with interleaved distances.
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.3f, 0.7f, 1.0f, 0.2f),
            Roughness = 0.1f,
            AlphaMode = World3DAlphaMode.Blend,
        }, out WorldMaterialHandle sortMat), Is.True);

        const int sortCount = 300;
        Span<WorldInstanceHandle> sortHandles = stackalloc WorldInstanceHandle[sortCount];
        Dictionary<int, float> distByInstanceSlot = [];
        for (int i = 0; i < sortCount; ++i)
        {
            // Scramble order deterministically so input is unsorted.
            int scrambled = (i * 73 + 19) % sortCount;
            float xDist = 10f + scrambled * 1.5f;
            var desc = new World3DInstanceDesc(sector, geom, sortMat, TransformAt(xDist, 0.0, 0.0, scale: 0.25f));
            Assert.That(scene.TryAddInstance(in desc, out sortHandles[i]), Is.True);
            distByInstanceSlot[sortHandles[i].Slot] = xDist;
        }

        World3DCamera cam = LookAt(new Vector3(0, 0, 0), new Vector3(10, 0, 0));
        using Texture output = h.CreateOutput(256, 256);
        h.SubmitFrame(output, cam);

        BufferSlice drawRefsSlice = scene.GetForwardDrawRefsGpuSlice();
        Assert.That(drawRefsSlice.IsEmpty, Is.False);
        byte[] refBytes = new byte[(int)drawRefsSlice.SizeBytes];
        h.System.ReadBuffer(drawRefsSlice.Buffer!, refBytes, drawRefsSlice.ByteOffset);
        ReadOnlySpan<uint> drawRefs = MemoryMarshal.Cast<byte, uint>(refBytes);

        Assert.That(scene.LastRecordedForward.CandidateCount, Is.EqualTo((uint)sortCount));
        float prevDist = float.PositiveInfinity;
        for (int i = 0; i < sortCount; ++i)
        {
            uint instSlot = drawRefs[i * 2];
            float d = distByInstanceSlot[(int)instSlot];
            Assert.That(d, Is.LessThanOrEqualTo(prevDist + 0.5f), $"Back-to-front violation at sorted index {i}");
            prevDist = d;
        }

        // Flip to front-to-back and verify monotonic ascending distance order.
        scene.Forward.SortFrontToBack = true;
        h.SubmitFrame(output, cam);
        BufferSlice f2bSlice = scene.GetForwardDrawRefsGpuSlice();
        h.System.ReadBuffer(f2bSlice.Buffer!, refBytes, f2bSlice.ByteOffset);
        drawRefs = MemoryMarshal.Cast<byte, uint>(refBytes);
        prevDist = 0f;
        for (int i = 0; i < sortCount; ++i)
        {
            float d = distByInstanceSlot[(int)drawRefs[i * 2]];
            Assert.That(d, Is.GreaterThanOrEqualTo(prevDist - 0.5f), $"Front-to-back violation at sorted index {i}");
            prevDist = d;
        }
        scene.Forward.SortFrontToBack = false;

        foreach (WorldInstanceHandle handle in sortHandles)
            Assert.That(scene.TryRemoveInstance(handle), Is.True);

        // 2. Back-to-front compositing + Hi-Z occlusion + custom ISurface + depth-safe glass refraction.
        h.System.Shaders.Compiler.RegisterVirtualModule(CustomSurfaceModule, CustomSurfaceModule + ".slang", CustomSurfaceSource);

        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.15f, 0.15f, 0.15f, 1.0f),
            Roughness = 0.8f,
            AlphaMode = World3DAlphaMode.Opaque,
        }, out WorldMaterialHandle darkWallMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            Emissive = new Vector3(12f, 2f, 0.5f),
            BaseColor = new Vector4(1f, 0.3f, 0.1f, 1f),
            AlphaMode = World3DAlphaMode.Opaque,
        }, out WorldMaterialHandle brightBgMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.95f, 0.98f, 1.0f, 0.15f),
            Roughness = 0.04f,
            AlphaMode = World3DAlphaMode.Blend,
            DoubleSided = true,
            Transmission = 0.92f,
            Ior = 1.52f,
        }, out WorldMaterialHandle refrGlassMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            AlphaMode = World3DAlphaMode.Blend,
            DoubleSided = true,
            SurfaceModule = CustomSurfaceModule,
        }, out WorldMaterialHandle customMat), Is.True);

        // Center background emissive wall at X = 20.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, brightBgMat,
            new World3DTransform(new World3DPosition(20, 0, 0), Quaternion.Identity, new Vector3(0.5f, 5f, 5f))), out _), Is.True);
        // Refractive glass pane at X = 10 facing camera.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, refrGlassMat,
            new World3DTransform(new World3DPosition(10, 0, 0), Quaternion.Identity, new Vector3(0.15f, 2f, 2f))), out _), Is.True);
        // Dark foreground pillar at X = 5, Y = 1.4 in front of glass edge to test refraction bleed protection.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, darkWallMat,
            new World3DTransform(new World3DPosition(5, 1.4, 0), Quaternion.Identity, new Vector3(0.4f, 0.45f, 3f))), out _), Is.True);
        // Hidden Blend pane behind a large opaque blocker squarely inside the right half of the FOV.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, darkWallMat,
            new World3DTransform(new World3DPosition(12, 4.2, 0), Quaternion.Identity, new Vector3(0.4f, 1.8f, 3.0f))), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, customMat,
            new World3DTransform(new World3DPosition(16, 4.2, 0), Quaternion.Identity, new Vector3(0.2f, 0.4f, 0.4f))), out WorldInstanceHandle hiddenCustom), Is.True);
        // Visible custom ISurface transparent pane at left (Y = -2.2, X = 11).
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, customMat,
            new World3DTransform(new World3DPosition(11, -2.2, 0), Quaternion.Identity, new Vector3(0.2f, 1.0f, 1.0f))), out _), Is.True);

        scene.Forward.UseHiZOcclusion = false;
        h.SubmitFrame(output, cam);
        scene.Forward.UseHiZOcclusion = true;
        h.SubmitFrame(output, cam);
        Assert.That(scene.LastRecordedForward.VisibleDraws, Is.EqualTo(3u), "Without Hi-Z, hidden pane passes frustum culling.");
        h.SubmitFrame(output, cam);
        Assert.That(scene.LastRecordedForward.BucketCount, Is.EqualTo(2));
        Assert.That(scene.LastRecordedForward.RefractionActive, Is.True);
        Assert.That(scene.LastRecordedForward.VisibleDraws, Is.EqualTo(2u), "With Hi-Z, hidden pane behind opaque wall is culled.");

        byte[] px = ReadPixels(h.System, output, 256);
        // Center of refractive glass sees warm orange background (zero black foreground bleeding).
        (double glassR, double glassG, double _) = AverageRegionRgb(px, 256, 118, 138, 118, 138);
        Assert.That(glassR, Is.GreaterThan(80.0));
        // Custom cyan ISurface visible pane sits toward left side of frame.
        (double _, double customG, double customB) = AverageRegionRgb(px, 256, 70, 95, 115, 140);
        Assert.That(customG + customB, Is.GreaterThan(150.0));
        _ = hiddenCustom;
    }

    /// <summary>Verifies volumetric clouds render sky density, build temporal history, cast same-frame cloud shadows, and stay stable at 500 km.</summary>
    [Test]
    public void VulkanCompact_VolumetricClouds_Sky_SameFrameShadow_Temporal_And_500Km()
    {
        using SceneHarness h = new(GraphicsBackend.VulkanCompact, textureHeapMb: 128);
        World3DScene scene = h.Scene;
        scene.Occlusion.Enabled = false;
        scene.Shadows.Enabled = false;
        scene.VoxelGi.Enabled = false;
        scene.VolumetricLight.Enabled = false;
        scene.Ssr.Enabled = false;
        scene.Forward.Enabled = false;
        scene.AutoExposure.Enabled = false;
        scene.Lighting = scene.Lighting with
        {
            DirectionToSun = Vector3.Normalize(new Vector3(0.3f, 0.2f, 0.85f)),
            SunIntensity = 12f,
            SunDiscBrightness = 0f,
        };

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        WorldGeometryHandle geom = UploadBoxGeometry(h.System, scene);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.85f, 0.85f, 0.85f, 1f),
            Roughness = 0.85f,
        }, out WorldMaterialHandle groundMat), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, groundMat,
            new World3DTransform(new World3DPosition(0, 0, -1), Quaternion.Identity, new Vector3(120, 120, 0.5f))), out WorldInstanceHandle groundInst), Is.True);

        World3DCamera skyCam = LookAt(new Vector3(0, 0, 2), new Vector3(20, 0, 12));
        World3DCamera groundCam = LookAt(new Vector3(0, 0, 12), new Vector3(20, 0, 0));
        using Texture output = h.CreateOutput(256, 256);

        // Baseline: coverage = 0 (clear sky & unshadowed ground).
        scene.VolumetricClouds.Coverage = 0f;
        scene.VolumetricClouds.CastShadows = true;
        h.SubmitFrame(output, groundCam);
        byte[] unshadowedGround = ReadPixels(h.System, output, 256);
        double clearGroundLuma = AverageRegionLuma(unshadowedGround, 256, 96, 160, 140, 220);

        // Enable thick clouds casting shadows on frame 1 (must darken same-frame ground even with CSM disabled!).
        scene.VolumetricClouds.Coverage = 0.92f;
        scene.VolumetricClouds.Density = 0.08f;
        scene.VolumetricClouds.ShadowStrength = 0.92f;
        h.SubmitFrame(output, groundCam);
        byte[] shadowedGround = ReadPixels(h.System, output, 256);
        double cloudGroundLuma = AverageRegionLuma(shadowedGround, 256, 96, 160, 140, 220);
        Assert.That(cloudGroundLuma, Is.LessThan(clearGroundLuma * 0.88), "Same-frame cloud shadow must darken sunlit ground immediately.");

        // Check sky clouds + CloudOpacity debug view + temporal history activation.
        h.SubmitFrame(output, skyCam);
        Assert.That(scene.LastRecordedVolumetricClouds.Enabled, Is.True);
        Assert.That(scene.LastRecordedVolumetricClouds.CastShadows, Is.True);
        h.SubmitFrame(output, skyCam);
        Assert.That(scene.LastRecordedVolumetricClouds.TemporalHistoryValid, Is.True);

        scene.LightingDebugView = World3DLightingDebugView.CloudOpacity;
        h.SubmitFrame(output, skyCam);
        byte[] opacityPx = ReadPixels(h.System, output, 256);
        Assert.That(AverageRegionLuma(opacityPx, 256, 16, 240, 16, 140), Is.GreaterThan(40.0), "CloudOpacity debug view must display nonzero sky cloud opacity.");

        // 500 km world coordinates stability check.
        scene.LightingDebugView = World3DLightingDebugView.None;
        Assert.That(scene.TryUpdateInstance(groundInst, new World3DInstanceDesc(sector, geom, groundMat,
            new World3DTransform(new World3DPosition(500_000, -320_000, -1), Quaternion.Identity, new Vector3(120, 120, 0.5f)))), Is.True);
        World3DCamera farCam = LookAt(new Vector3(500_000, -320_000, 4), new Vector3(500_020, -320_000, 10));
        h.SubmitFrame(output, farCam);
        byte[] farPx = ReadPixels(h.System, output, 256);
        Assert.That(MaxChannel(farPx), Is.GreaterThan(10));
    }

    /// <summary>Verifies shadowed volumetric light shafts produce bright scattering vs shadowed cones and stay valid at 500 km.</summary>
    [Test]
    public void VulkanCompact_VolumetricLight_CreatesShaftsAndRunsAt500Km()
    {
        using SceneHarness h = new(GraphicsBackend.VulkanCompact, textureHeapMb: 128);
        World3DScene scene = h.Scene;
        scene.Occlusion.Enabled = false;
        scene.VoxelGi.Enabled = false;
        scene.VolumetricClouds.Enabled = false;
        scene.Ssr.Enabled = false;
        scene.Forward.Enabled = false;
        scene.AutoExposure.Enabled = false;
        scene.Lighting = scene.Lighting with
        {
            DirectionToSun = Vector3.Normalize(new Vector3(0.9f, 0.0f, 0.25f)),
            SunIntensity = 18f,
            SunDiscBrightness = 0f,
            SkyExposure = 0.35f,
        };

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        WorldGeometryHandle geom = UploadBoxGeometry(h.System, scene);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.1f, 0.1f, 0.12f, 1f),
            Roughness = 0.9f,
        }, out WorldMaterialHandle blockerMat), Is.True);

        // Tall blocker at X = 20 Y = 0 casting shadow toward camera, open sun to left/right.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, blockerMat,
            new World3DTransform(new World3DPosition(16, 0, 2), Quaternion.Identity, new Vector3(1.5f, 1.5f, 8f))), out WorldInstanceHandle pillar), Is.True);

        World3DCamera cam = LookAt(new Vector3(0, 0, 3), new Vector3(20, 0, 3));
        using Texture output = h.CreateOutput(256, 256);

        scene.VolumetricLight.Enabled = false;
        h.SubmitFrame(output, cam);
        byte[] offPx = ReadPixels(h.System, output, 256);
        double offLuma = AverageRegionLuma(offPx, 256, 16, 80, 40, 200);

        scene.VolumetricLight.Enabled = true;
        scene.VolumetricLight.Intensity = 1.6f;
        scene.VolumetricLight.BaseDensity = 0.045f;
        scene.VolumetricLight.AmbientFogIntensity = 0.02f;
        h.SubmitFrame(output, cam);
        Assert.That(scene.LastRecordedVolumetricLight.Enabled, Is.True);
        byte[] onPx = ReadPixels(h.System, output, 256);
        double onSideShaftLuma = AverageRegionLuma(onPx, 256, 16, 80, 40, 200);
        Assert.That(onSideShaftLuma, Is.GreaterThan(offLuma + 8.0), "Volumetric light should noticeably brighten sunlit shafts.");

        // 500 km world coordinates test.
        Assert.That(scene.TryUpdateInstance(pillar, new World3DInstanceDesc(sector, geom, blockerMat,
            new World3DTransform(new World3DPosition(500_016, -320_000, 2), Quaternion.Identity, new Vector3(1.5f, 1.5f, 8f)))), Is.True);
        h.SubmitFrame(output, LookAt(new Vector3(500_000, -320_000, 3), new Vector3(500_020, -320_000, 3)));
        byte[] farPx = ReadPixels(h.System, output, 256);
        Assert.That(MaxChannel(farPx), Is.GreaterThan(10));
    }

    /// <summary>Verifies Hi-Z SSR works even when Phase 2 Occlusion culling is disabled, reflects bright walls accurately, and stays stable at 500 km.</summary>
    [Test]
    public void VulkanCompact_Ssr_ReflectsWithHiZ_WhenOcclusionDisabled_And_RunsAt500Km()
    {
        using SceneHarness h = new(GraphicsBackend.VulkanCompact, textureHeapMb: 128);
        World3DScene scene = h.Scene;
        scene.Occlusion.Enabled = false;
        scene.Shadows.Enabled = false;
        scene.VoxelGi.Enabled = false;
        scene.VolumetricClouds.Enabled = false;
        scene.VolumetricLight.Enabled = false;
        scene.Forward.Enabled = false;
        scene.AutoExposure.Enabled = false;

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        WorldGeometryHandle geom = UploadBoxGeometry(h.System, scene);

        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.06f, 0.06f, 0.07f, 1f),
            Metallic = 0.85f,
            Roughness = 0.06f,
        }, out WorldMaterialHandle mirrorFloorMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(1f, 0.55f, 0.1f, 1f),
            Emissive = new Vector3(10f, 3.5f, 0.4f),
            Roughness = 0.9f,
        }, out WorldMaterialHandle emissiveWallMat), Is.True);

        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, mirrorFloorMat,
            new World3DTransform(new World3DPosition(10, 0, -0.25), Quaternion.Identity, new Vector3(24, 16, 0.25f))), out WorldInstanceHandle floor), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, emissiveWallMat,
            new World3DTransform(new World3DPosition(15, 0, 2.5), Quaternion.Identity, new Vector3(0.5f, 8f, 2.5f))), out WorldInstanceHandle wall), Is.True);

        World3DCamera cam = LookAt(new Vector3(0, 0, 2.2f), new Vector3(11, 0, -0.1f));
        using Texture output = h.CreateOutput(256, 256);

        scene.Ssr.Enabled = false;
        h.SubmitFrame(output, cam);
        byte[] ssrOff = ReadPixels(h.System, output, 256);
        double offFloorLuma = AverageRegionLuma(ssrOff, 256, 70, 186, 150, 235);

        scene.Ssr.Enabled = true;
        scene.Ssr.HalfResolution = false;
        h.SubmitFrame(output, cam);
        Assert.That(scene.LastRecordedOcclusion.Enabled, Is.False);
        Assert.That(scene.LastRecordedSsr.Enabled, Is.True);
        Assert.That(scene.LastRecordedSsr.HiZActive, Is.True, "SSR must reuse built Phase 2 Hi-Z pyramid even when Occlusion.Enabled is false.");

        // Frame 2 activates temporal resolve.
        h.SubmitFrame(output, cam);
        Assert.That(scene.LastRecordedSsr.TemporalHistoryValid, Is.True);
        byte[] ssrOn = ReadPixels(h.System, output, 256);
        (double onR, double _, double _) = AverageRegionRgb(ssrOn, 256, 70, 186, 150, 235);
        double onFloorLuma = AverageRegionLuma(ssrOn, 256, 70, 186, 150, 235);
        Assert.That(onFloorLuma, Is.GreaterThan(offFloorLuma + 15.0));
        Assert.That(onR, Is.GreaterThan(60.0));

        // Verify SSR reflection debug view.
        scene.LightingDebugView = World3DLightingDebugView.SsrReflection;
        h.SubmitFrame(output, cam);
        byte[] dbgPx = ReadPixels(h.System, output, 256);
        Assert.That(MaxChannel(dbgPx), Is.GreaterThan(30));
        scene.LightingDebugView = World3DLightingDebugView.None;

        // 500 km world coordinates SSR test.
        Assert.That(scene.TryUpdateInstance(floor, new World3DInstanceDesc(sector, geom, mirrorFloorMat,
            new World3DTransform(new World3DPosition(500_010, -320_000, -0.25), Quaternion.Identity, new Vector3(24, 16, 0.25f)))), Is.True);
        Assert.That(scene.TryUpdateInstance(wall, new World3DInstanceDesc(sector, geom, emissiveWallMat,
            new World3DTransform(new World3DPosition(500_015, -320_000, 2.5), Quaternion.Identity, new Vector3(0.5f, 8f, 2.5f)))), Is.True);
        World3DCamera farCam = LookAt(new Vector3(500_000, -320_000, 2.2f), new Vector3(500_011, -320_000, -0.1f));
        h.SubmitFrame(output, farCam);
        byte[] farPx = ReadPixels(h.System, output, 256);
        Assert.That(MaxChannel(farPx), Is.GreaterThan(40));
    }

    /// <summary>Verifies the prefiltered SSR color pyramid blurs reflections smoothly as roughness grows and skips matte surfaces.</summary>
    [Test]
    public void VulkanCompact_Ssr_PrefilteredBlurScalesWithRoughness_And_MatteSkipsSsr()
    {
        using SceneHarness h = new(GraphicsBackend.VulkanCompact, textureHeapMb: 128);
        World3DScene scene = h.Scene;
        scene.Occlusion.Enabled = false;
        scene.Shadows.Enabled = false;
        scene.VoxelGi.Enabled = false;
        scene.VolumetricClouds.Enabled = false;
        scene.VolumetricLight.Enabled = false;
        scene.Forward.Enabled = false;
        scene.AutoExposure.Enabled = false;
        scene.Ssr.Enabled = true;
        scene.Ssr.HalfResolution = false;
        scene.Ssr.TemporalBlend = 0f;
        Assert.That(scene.Ssr.MaxRoughness, Is.EqualTo(1.0f));

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        WorldGeometryHandle geom = UploadBoxGeometry(h.System, scene);

        var floorDesc = new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.06f, 0.06f, 0.07f, 1f),
            Metallic = 0.85f,
            Roughness = 0.04f,
        };
        Assert.That(scene.TryRegisterMaterial(in floorDesc, out WorldMaterialHandle floorMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.02f, 0.02f, 0.025f, 1f),
            Roughness = 0.9f,
        }, out WorldMaterialHandle darkWallMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(1f, 0.6f, 0.15f, 1f),
            Emissive = new Vector3(14f, 5f, 0.8f),
            Roughness = 0.9f,
        }, out WorldMaterialHandle brightBarMat), Is.True);

        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, floorMat,
            new World3DTransform(new World3DPosition(10, 0, -0.25), Quaternion.Identity, new Vector3(24, 16, 0.25f))), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, darkWallMat,
            new World3DTransform(new World3DPosition(15, 0, 2.5), Quaternion.Identity, new Vector3(0.5f, 12f, 2.5f))), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, brightBarMat,
            new World3DTransform(new World3DPosition(14, 0, 2.5), Quaternion.Identity, new Vector3(0.2f, 0.45f, 2.5f))), out _), Is.True);

        World3DCamera cam = LookAt(new Vector3(0, 0, 2.2f), new Vector3(11, 0, -0.1f));
        using Texture output = h.CreateOutput(256, 256);

        scene.LightingDebugView = World3DLightingDebugView.SsrReflection;
        h.SubmitFrame(output, cam);
        byte[] sharpPx = ReadPixels(h.System, output, 256);
        double sharpCenter = AverageRegionLuma(sharpPx, 256, 120, 136, 155, 220);
        double sharpFlanks = 0.5 * (AverageRegionLuma(sharpPx, 256, 64, 96, 155, 220) + AverageRegionLuma(sharpPx, 256, 160, 192, 155, 220));
        double sharpContrast = sharpCenter - sharpFlanks;
        Assert.That(sharpCenter, Is.GreaterThan(40.0));
        Assert.That(sharpContrast, Is.GreaterThan(25.0));

        Assert.That(scene.TryUpdateMaterial(floorMat, floorDesc with { Roughness = 0.30f }), Is.True);
        h.SubmitFrame(output, cam);
        h.SubmitFrame(output, cam);
        byte[] roughPx = ReadPixels(h.System, output, 256);
        double roughCenter = AverageRegionLuma(roughPx, 256, 120, 136, 155, 220);
        double roughFlanks = 0.5 * (AverageRegionLuma(roughPx, 256, 64, 96, 155, 220) + AverageRegionLuma(roughPx, 256, 160, 192, 155, 220));
        double roughContrast = roughCenter - roughFlanks;
        Assert.That(roughCenter, Is.GreaterThan(5.0));
        Assert.That(roughContrast, Is.LessThan(sharpContrast * 0.75), "Prefiltered SSR pyramid must blur rough reflections across mip levels.");

        Assert.That(scene.TryUpdateMaterial(floorMat, floorDesc with { Roughness = 1.0f }), Is.True);
        scene.LightingDebugView = World3DLightingDebugView.SsrConfidence;
        h.SubmitFrame(output, cam);
        h.SubmitFrame(output, cam);
        byte[] matteConfPx = ReadPixels(h.System, output, 256);
        double matteFloorConf = AverageRegionLuma(matteConfPx, 256, 70, 186, 155, 220);
        Assert.That(matteFloorConf, Is.LessThan(2.0), "Fully rough surfaces at MaxRoughness = 1.0 must have zero SSR confidence.");

        scene.Ssr.MaxRoughness = 0.5f;
        Assert.That(scene.TryUpdateMaterial(floorMat, floorDesc with { Roughness = 0.65f }), Is.True);
        h.SubmitFrame(output, cam);
        h.SubmitFrame(output, cam);
        byte[] cutoffConfPx = ReadPixels(h.System, output, 256);
        double cutoffFloorConf = AverageRegionLuma(cutoffConfPx, 256, 70, 186, 155, 220);
        Assert.That(cutoffFloorConf, Is.LessThan(2.0), "Surfaces above custom MaxRoughness must have zero SSR confidence.");
    }

    /// <summary>Regression test verifying all Phase 4 features enabled together preserve opaque lighting cleanly across Forward snapshot and SSR.</summary>
    [Test]
    public void VulkanCompact_AllPhase4EnabledTogether_PreservesOpaqueLightingAndCompositesCleanly()
    {
        using SceneHarness h = new(GraphicsBackend.VulkanCompact, textureHeapMb: 128);
        World3DScene scene = h.Scene;
        Assert.That(scene.VolumetricClouds.Enabled, Is.True);
        Assert.That(scene.VolumetricLight.Enabled, Is.True);
        Assert.That(scene.Ssr.Enabled, Is.True);
        Assert.That(scene.Forward.Enabled, Is.True);

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        WorldGeometryHandle geom = UploadBoxGeometry(h.System, scene);

        // Warm orange emissive/lit opaque wall filling background left and glossy floor, plus refractive glass pane on right.
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.85f, 0.25f, 0.08f, 1f),
            Emissive = new Vector3(2.4f, 0.45f, 0.12f),
            Roughness = 0.7f,
        }, out WorldMaterialHandle orangeWallMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.12f, 0.14f, 0.18f, 1f),
            Metallic = 0.6f,
            Roughness = 0.08f,
        }, out WorldMaterialHandle glossyFloorMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = new Vector4(0.7f, 0.9f, 1.0f, 0.2f),
            Roughness = 0.05f,
            AlphaMode = World3DAlphaMode.Blend,
            DoubleSided = true,
            Transmission = 0.85f,
            Ior = 1.5f,
        }, out WorldMaterialHandle glassMat), Is.True);

        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, orangeWallMat,
            new World3DTransform(new World3DPosition(16, 0, 2), Quaternion.Identity, new Vector3(0.5f, 10f, 3f))), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, glossyFloorMat,
            new World3DTransform(new World3DPosition(10, 0, -0.5), Quaternion.Identity, new Vector3(12f, 12f, 0.25f))), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, glassMat,
            new World3DTransform(new World3DPosition(9, 2.5, 1.2), Quaternion.Identity, new Vector3(0.15f, 1.5f, 1.5f))), out _), Is.True);

        World3DCamera cam = LookAt(new Vector3(0, 0, 2f), new Vector3(16, 0, 1.2f));
        using Texture output = h.CreateOutput(256, 256);
        h.SubmitFrame(output, cam);
        h.SubmitFrame(output, cam);

        Assert.That(scene.LastRecordedForward.Enabled, Is.True);
        Assert.That(scene.LastRecordedVolumetricClouds.TemporalHistoryValid, Is.True);
        Assert.That(scene.LastRecordedSsr.TemporalHistoryValid, Is.True);

        byte[] px = ReadPixels(h.System, output, 256);
        // Uncovered left side of the orange wall MUST retain warm red/orange color (not black and not normal-map cyan/magenta!).
        (double wallR, double wallG, double wallB) = AverageRegionRgb(px, 256, 40, 80, 90, 130);
        Assert.That(wallR, Is.GreaterThan(wallG + 40.0), "Opaque background must not be lost or overwritten by GBuffer normal aliasing.");
        Assert.That(wallR, Is.GreaterThan(wallB + 50.0));
        // Glass pane region on right side must show bright warm refracted background rather than black.
        (double glassR, double _, double _) = AverageRegionRgb(px, 256, 175, 205, 95, 135);
        Assert.That(glassR, Is.GreaterThan(70.0));
    }

    private static WorldGeometryHandle UploadBoxGeometry(RenderingSystem system, World3DScene scene)
    {
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle geom), Is.True);
        ModelGeometry model = ModelGeometry.Create<VertexPBR>(system, s_boxVertices, s_boxIndices, s_boxMin, s_boxMax, "phase4 box");
        if (!scene.TryQueueGeometryLod(geom, 0, model))
        {
            model.Dispose();
            Assert.Fail("Failed to queue box LOD 0.");
        }
        return geom;
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
        new(new Vector3(-1, -1, -1), -Vector3.UnitZ, Vector2.Zero),
        new(new Vector3(-1, 1, -1), -Vector3.UnitZ, Vector2.UnitY),
        new(new Vector3(1, 1, -1), -Vector3.UnitZ, Vector2.One),
        new(new Vector3(1, -1, -1), -Vector3.UnitZ, Vector2.UnitX),
        new(new Vector3(-1, -1, 1), Vector3.UnitZ, Vector2.Zero),
        new(new Vector3(1, -1, 1), Vector3.UnitZ, Vector2.UnitX),
        new(new Vector3(1, 1, 1), Vector3.UnitZ, Vector2.One),
        new(new Vector3(-1, 1, 1), Vector3.UnitZ, Vector2.UnitY),
        new(new Vector3(1, -1, -1), Vector3.UnitX, Vector2.Zero),
        new(new Vector3(1, 1, -1), Vector3.UnitX, Vector2.UnitX),
        new(new Vector3(1, 1, 1), Vector3.UnitX, Vector2.One),
        new(new Vector3(1, -1, 1), Vector3.UnitX, Vector2.UnitY),
        new(new Vector3(-1, -1, -1), -Vector3.UnitX, Vector2.Zero),
        new(new Vector3(-1, -1, 1), -Vector3.UnitX, Vector2.UnitY),
        new(new Vector3(-1, 1, 1), -Vector3.UnitX, Vector2.One),
        new(new Vector3(-1, 1, -1), -Vector3.UnitX, Vector2.UnitX),
        new(new Vector3(1, 1, -1), Vector3.UnitY, Vector2.Zero),
        new(new Vector3(-1, 1, -1), Vector3.UnitY, Vector2.UnitX),
        new(new Vector3(-1, 1, 1), Vector3.UnitY, Vector2.One),
        new(new Vector3(1, 1, 1), Vector3.UnitY, Vector2.UnitY),
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

        public World3DView SubmitFrame(Texture output, World3DCamera camera)
        {
            Context.BeginFrame();
            RenderGraphTarget target = RenderGraphTarget.FromTexture(output);
            Assert.That(Scene.TryRecordFrame(Context, camera, new Extent2D(output.Extent.X, output.Extent.Y),
                out World3DView view, target, RGUsage.TransferRead, renderWorld: true, deltaTime: 1f / 30f), Is.True);
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
