using System.Numerics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>Exercises World3D Phase 2 Hi-Z two-phase occlusion, tiered CSM shadows, far horizon shadows, and RSM.</summary>
[TestFixture]
public sealed class TestWorld3DPhase2
{
    private const string MaskSurfaceModule = "TestPhase2MaskSurface";
    private const string MaskSurfaceSource = """
        #language slang 2025
        module TestPhase2MaskSurface;
        import World3DSurface;

        public struct TestPhase2MaskSurface : ISurface
        {
            public override float4 getBaseColor(SurfaceInput input)
            {
                float alpha = input.UV.x < 0.5 ? 1.0 : 0.0;
                return float4(input.Material.BaseColor.rgb, alpha);
            }
        };
        """;

    private static readonly Vector3 s_quadMin = new(0, -1f, -1f);
    private static readonly Vector3 s_quadMax = new(0, 1f, 1f);
    private static readonly VertexPBR[] s_panelVertices =
    [
        new(new Vector3(0, -1f, -1f), -Vector3.UnitX, new Vector2(0, 1)),
        new(new Vector3(0, -1f, 1f), -Vector3.UnitX, Vector2.Zero),
        new(new Vector3(0, 1f, 1f), -Vector3.UnitX, new Vector2(1, 0)),
        new(new Vector3(0, 1f, -1f), -Vector3.UnitX, new Vector2(1, 1)),
    ];
    private static ReadOnlySpan<uint> PanelIndices => [0u, 1u, 2u, 0u, 2u, 3u];

    private static readonly Vector3 s_groundMin = new(-500f, -500f, 0f);
    private static readonly Vector3 s_groundMax = new(500f, 500f, 0f);
    private static readonly VertexPBR[] s_groundVertices =
    [
        new(new Vector3(-500f, -500f, 0f), Vector3.UnitZ, Vector2.Zero),
        new(new Vector3(500f, -500f, 0f), Vector3.UnitZ, Vector2.UnitX),
        new(new Vector3(500f, 500f, 0f), Vector3.UnitZ, Vector2.One),
        new(new Vector3(-500f, 500f, 0f), Vector3.UnitZ, Vector2.UnitY),
    ];
    // Clockwise from +Z downward in left-handed +X forward, +Y right, +Z up.
    private static ReadOnlySpan<uint> GroundIndices => [0u, 1u, 2u, 0u, 2u, 3u];

    private static readonly Vector3 s_boxMin = new(-1f, -1f, -1f);
    private static readonly Vector3 s_boxMax = new(1f, 1f, 1f);
    private static readonly VertexPBR[] s_boxVertices = CreateBoxVertices();
    private static readonly uint[] s_boxIndices = CreateBoxIndices();

    /// <summary>Compiles all Phase 2 compute and composed shadow/RSM kernels across every shader backend target.</summary>
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Dxil)]
    [TestCase(ShaderTarget.MetalSource)]
    public void ValidateShader_CompilesPhase2KernelsAndShadowSurfaces(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
            Assert.Ignore($"The bundled shader compiler cannot emit {target} on this host.");

        using ShaderCompiler compiler = new(target, new ShaderCompilerOptions { SearchPaths = [] });
        ReadOnlySpan<string> modules =
        [
            "World3DSceneData",
            "World3DOcclusionCommon",
            "World3DShadowSample",
            "World3DSurface",
            "World3DPbrStandard",
            "World3DHiZBuild",
            "World3DOcclusionPrepare",
            "World3DOcclusionRetest",
            "World3DShadowMinMaxReduce",
            "World3DShadowMinMaxDilate",
            "World3DFarHeightBuild",
            "World3DShadowDepth",
            "World3DRsm",
        ];
        foreach (string module in modules)
        {
            string file = module + ".slang";
            using StreamReader source = new(typeof(World3DScene).Assembly.GetManifestResourceStream(file)
                ?? throw new InvalidOperationException($"Missing embedded shader {file}."));
            compiler.RegisterVirtualModule(module, file, source.ReadToEnd());
        }
        compiler.RegisterVirtualModule(MaskSurfaceModule, MaskSurfaceModule + ".slang", MaskSurfaceSource);

        ReadOnlySpan<(string Module, uint RootBytes)> computeKernels =
        [
            ("World3DHiZBuild", 32),
            ("World3DOcclusionPrepare", 64),
            ("World3DOcclusionRetest", 60),
            ("World3DShadowMinMaxReduce", 24),
            ("World3DShadowMinMaxDilate", 24),
            ("World3DFarHeightBuild", 48),
        ];
        foreach ((string module, uint bytes) in computeKernels)
            Assert.That(compiler.Compile(module, ShaderPipelineKind.Compute).Reflection.Root?.SizeBytes, Is.EqualTo(bytes), module);

        ReadOnlySpan<(string Template, uint RootBytes)> templates = [("World3DShadowDepth", 40), ("World3DRsm", 80)];
        ReadOnlySpan<string> surfaces = ["World3DPbrStandard", MaskSurfaceModule];
        ReadOnlySpan<string> specializations = ["false", "true"];
        foreach ((string template, uint rootBytes) in templates)
        {
            foreach (string surface in surfaces)
            {
                foreach (string alphaTest in specializations)
                {
                    CompiledShader shader = compiler.CompileComposed(template, surface, ShaderPipelineKind.Graphics,
                        new ShaderCompileOptions { SpecializationArgs = [alphaTest] });
                    Assert.That(shader.Reflection.Root?.SizeBytes, Is.EqualTo(rootBytes), $"{template}:{surface}, alphaTest={alphaTest}");
                }
            }
        }
    }

    /// <summary>Verifies tiered CSM caching, mutation/sun/drift invalidation, far-distance texel snapping, and discard rollback on NoGPU.</summary>
    [Test]
    public void NoGpuTieredCachingFarSnappingAndDiscardRollback()
    {
        using SceneHarness harness = new(GraphicsBackend.NoGPU);
        using Texture output = harness.CreateOutput(128, 128);
        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = true;
        scene.Shadows.CascadeResolution = 256;
        scene.Shadows.MaxDistance = 120f;
        scene.Shadows.CascadeUpdateIntervals = [1, 1, 2, 4];
        scene.Shadows.FarField.Enabled = false;
        scene.Shadows.FarField.StaticFarRefreshIntervalFrames = 8;

        World3DCamera farCamera = new(new World3DPosition(512_000.0, 256_000.0, 20.0), Quaternion.Identity, MathF.PI / 3f, 0.25f);

        harness.SubmitFrame(output, farCamera);
        Assert.That(scene.LastRecordedShadows.UpdatedCascadeMask & 0x1Fu, Is.EqualTo(0x1Fu));
        Assert.That(scene.LastRecordedShadows.CachedCascadeMask, Is.EqualTo(0u));

        // Sub-texel movement (1cm at 500km away) keeps cached cascades 2..4 intact.
        World3DCamera subTexelCamera = farCamera with { Eye = new World3DPosition(512_000.01, 256_000.01, 20.0) };
        harness.SubmitFrame(output, subTexelCamera);
        Assert.That(scene.LastRecordedShadows.CachedCascadeMask & 0b11100u, Is.EqualTo(0b11100u));

        // Discarding a recorded frame rolls back cache updates without advancing cached state.
        harness.RecordFrame(output, subTexelCamera);
        Assert.That(scene.LastRecordedShadows.UpdatedCascadeMask & 0b00011u, Is.EqualTo(0b00011u));
        harness.Context.DiscardFrame();

        // Next submitted frame invalidates after DiscardVersion incremented cleanly.
        harness.SubmitFrame(output, subTexelCamera);
        Assert.That(scene.LastRecordedShadows.UpdatedCascadeMask & 0x1Fu, Is.EqualTo(0x1Fu));

        // Advance one calm frame to populate cache, then invalidate via scene instance mutation.
        harness.SubmitFrame(output, subTexelCamera);
        Assert.That(scene.LastRecordedShadows.CachedCascadeMask & 0b11100u, Is.EqualTo(0b11100u));

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc(), out WorldMaterialHandle mat), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle geom), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, geom, mat, TransformAt(512_020.0, 256_000.0, 20.0)), out WorldInstanceHandle inst), Is.True);

        harness.SubmitFrame(output, subTexelCamera);
        Assert.That(scene.LastRecordedShadows.UpdatedCascadeMask & 0x1Fu, Is.EqualTo(0x1Fu), "Adding an instance invalidates all shadow slices.");

        harness.SubmitFrame(output, subTexelCamera);
        Assert.That(scene.LastRecordedShadows.CachedCascadeMask & 0b11100u, Is.EqualTo(0b11100u));

        // Sun change invalidates all cascades.
        scene.Lighting = scene.Lighting with { DirectionToSun = ProceduralSkyUtility.GetDirectionToSun(14.5f) };
        harness.SubmitFrame(output, subTexelCamera);
        Assert.That(scene.LastRecordedShadows.UpdatedCascadeMask & 0x1Fu, Is.EqualTo(0x1Fu));

        // Fast teleport invalidates drifted cascades.
        harness.SubmitFrame(output, subTexelCamera);
        World3DCamera teleported = farCamera with { Eye = new World3DPosition(513_500.0, 256_000.0, 20.0) };
        harness.SubmitFrame(output, teleported);
        Assert.That(scene.LastRecordedShadows.UpdatedCascadeMask & 0x1Fu, Is.EqualTo(0x1Fu));
        Assert.That(scene.TryRemoveInstance(inst), Is.True);
    }

    /// <summary>Verifies two-phase Hi-Z occlusion culls occluded objects then resurrects them in-frame when the occluder moves, matching reference pixels.</summary>
    [Test]
    public void VulkanGpuTwoPhaseHiZResurrectsDisoccludedGeometryWithZeroDiff()
    {
        const int size = 128;
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture outputOcc = harness.CreateOutput(size, size);
        using Texture outputRef = harness.CreateOutput(size, size);
        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = false;
        scene.Occlusion.Enabled = true;
        scene.Occlusion.TwoPhase = true;
        scene.Lighting = scene.Lighting with { DirectionToSun = Vector3.Normalize(new Vector3(-1f, 0f, 0.5f)), AmbientFloor = 0.5f };

        World3DCamera camera = new(default, Quaternion.Identity, MathF.PI / 3f, 0.1f);

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_quadMin, s_quadMax, out WorldGeometryHandle panelGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(panelGeom, 0, s_panelVertices, PanelIndices), Is.True);

        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0f, 0f, 1f, 1f) }, out WorldMaterialHandle blueMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0f, 1f, 0f, 1f) }, out WorldMaterialHandle greenMat), Is.True);

        // Wall A cleanly covers center view; green Panel B sits completely behind Wall A.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, panelGeom, blueMat, TransformAt(5.0, 0.0, 0.0, 3.0f)), out WorldInstanceHandle wallA), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, panelGeom, greenMat, TransformAt(12.0, 0.0, 0.0, 0.8f)), out _), Is.True);

        // Frame 0 builds initial Hi-Z. Frame 1 tests against PrevHiZ and finds Panel B occluded.
        harness.SubmitFrame(outputOcc, camera);
        harness.SubmitFrame(outputOcc, camera);
        Assert.That(scene.TryReadOcclusionCounts(out uint phase1, out uint occluded, out uint phase2), Is.True);
        Assert.That(phase1, Is.EqualTo(1u));
        Assert.That(occluded, Is.EqualTo(1u));
        Assert.That(phase2, Is.EqualTo(0u));

        // Frame 2: Move Wall A laterally out of the way in the SAME frame where PrevHiZ still shows Wall A at center.
        Assert.That(scene.TryUpdateInstance(wallA, new World3DInstanceDesc(sector, panelGeom, blueMat, TransformAt(5.0, 8.0, 0.0, 3.0f))), Is.True);
        harness.SubmitFrame(outputOcc, camera);
        Assert.That(scene.TryReadOcclusionCounts(out uint p1After, out uint occAfter, out uint p2After), Is.True);
        Assert.That(occAfter, Is.EqualTo(1u), "PrevHiZ still saw Wall A blocking Panel B at center.");
        Assert.That(p2After, Is.EqualTo(1u), "Phase 2 retest immediately resurrected Panel B in the same frame.");

        byte[] occPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outputOcc, occPixels);
        ReadOnlySpan<byte> center = occPixels.AsSpan((64 * size + 64) * 4, 4);
        Assert.That(center[1], Is.GreaterThan(100), "Resurrected green Panel B rendered cleanly at center without single-frame flicker.");
        Assert.That(center[2], Is.LessThan(40));

        // Compare against reference with Occlusion disabled.
        scene.Occlusion.Enabled = false;
        harness.SubmitFrame(outputRef, camera);
        byte[] refPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outputRef, refPixels);
        AssertMaxPixelDiff(occPixels, refPixels, 0, "Two-phase Hi-Z resurrection must produce zero pixel difference vs disabled occlusion.");
    }

    /// <summary>Verifies NPOT Hi-Z conservative tail handling across odd viewport dimensions.</summary>
    [Test]
    public void VulkanGpuOddViewportHiZMatchesDisabledOcclusion()
    {
        const int w = 97;
        const int h = 65;
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture outOcc = harness.CreateOutput(w, h);
        using Texture outRef = harness.CreateOutput(w, h);
        World3DScene scene = harness.Scene;
        scene.Shadows.Enabled = false;
        scene.Occlusion.Enabled = true;
        scene.Occlusion.TwoPhase = true;
        scene.Lighting = scene.Lighting with { DirectionToSun = Vector3.Normalize(new Vector3(-1f, 0f, 0.5f)) };

        World3DCamera camera = new(default, Quaternion.Identity, MathF.PI / 3f, 0.1f);
        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_quadMin, s_quadMax, out WorldGeometryHandle panelGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(panelGeom, 0, s_panelVertices, PanelIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.9f, 0.2f, 0.2f, 1f) }, out WorldMaterialHandle redMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.2f, 0.9f, 0.2f, 1f) }, out WorldMaterialHandle greenMat), Is.True);

        // Edge-straddling instances exercising the right/bottom NPOT 3-texel tails.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, panelGeom, redMat, TransformAt(5.0, 2.5, 1.5, 1.0f)), out _), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, panelGeom, greenMat, TransformAt(9.0, 4.2, 2.5, 1.5f)), out _), Is.True);

        harness.SubmitFrame(outOcc, camera);
        harness.SubmitFrame(outOcc, camera);
        Assert.That(scene.LastRecordedOcclusion.HiZExtent, Is.EqualTo(new Extent2D(48, 32)));

        byte[] occPixels = new byte[w * h * 4];
        harness.System.ReadTexture(outOcc, occPixels);

        scene.Occlusion.Enabled = false;
        harness.SubmitFrame(outRef, camera);
        byte[] refPixels = new byte[w * h * 4];
        harness.System.ReadTexture(outRef, refPixels);
        AssertMaxPixelDiff(occPixels, refPixels, 0, "Odd viewport Hi-Z must match disabled occlusion with zero diff.");
    }

    /// <summary>Verifies CSM shadows, Min-Max early-out equivalence, CastsShadow=false, and custom ISurface alpha-masked shadow cutout.</summary>
    [Test]
    public void VulkanGpuCsmShadowsMinMaxEquivalenceCastsShadowAndMaskedSurface()
    {
        const int size = 128;
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture outMinMaxOn = harness.CreateOutput(size, size);
        using Texture outMinMaxOff = harness.CreateOutput(size, size);
        using Texture outNoCast = harness.CreateOutput(size, size);
        using Texture outMasked = harness.CreateOutput(size, size);

        World3DScene scene = harness.Scene;
        scene.Occlusion.Enabled = false;
        scene.Shadows.Enabled = true;
        scene.Shadows.CascadeResolution = 256;
        scene.Shadows.MaxDistance = 80f;
        scene.Shadows.UseMinMaxAcceleration = true;
        scene.Shadows.FarField.Enabled = false;
        scene.Shadows.FarField.EnableStaticFarCascade = false;

        // Overhead slightly tilted sun from -Y/+Z so shadows fall along +Y across the ground plane.
        Vector3 sunDir = Vector3.Normalize(new Vector3(0f, -0.45f, 0.89f));
        scene.Lighting = scene.Lighting with { DirectionToSun = sunDir, SkyExposure = 0.12f, AmbientFloor = 0.02f, SunIntensity = 12f };

        // Camera high at (0, 0, 18) looking toward +X and down at ground.
        Quaternion camRot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.0f);
        World3DCamera camera = new(new World3DPosition(0.0, 0.0, 18.0), camRot, MathF.PI / 3f, 0.25f);

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_groundMin, s_groundMax, out WorldGeometryHandle groundGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(groundGeom, 0, s_groundVertices, GroundIndices), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle boxGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(boxGeom, 0, s_boxVertices, s_boxIndices), Is.True);

        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.45f, 0.45f, 0.45f, 1f), Roughness = 0.9f }, out WorldMaterialHandle groundMat), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = Vector4.One, Roughness = 0.9f }, out WorldMaterialHandle whiteMat), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, groundGeom, groundMat, TransformAt(0, 0, 0), CastsShadow: false), out _), Is.True);

        // Tall box caster offset to (8, -2, 3) casts shadow toward (8, +0.5, 0) while leaving (8, -6, 0) sunlit.
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, boxGeom, whiteMat,
            new World3DTransform(new World3DPosition(8.0, -2.0, 4.0), Quaternion.Identity, new Vector3(2.0f, 1.0f, 4.0f)), CastsShadow: true),
            out WorldInstanceHandle caster), Is.True);

        scene.Shadows.UseMinMaxAcceleration = true;
        harness.SubmitFrame(outMinMaxOn, camera);
        byte[] onPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outMinMaxOn, onPixels);

        scene.Shadows.UseMinMaxAcceleration = false;
        harness.SubmitFrame(outMinMaxOff, camera);
        byte[] offPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outMinMaxOff, offPixels);
        AssertMaxPixelDiff(onPixels, offPixels, 1, "Min-Max acceleration must match unfiltered PCF shadow output.");

        // Verify shadow contrast exists and disappears when CastsShadow = false.
        Assert.That(scene.TryUpdateInstance(caster, new World3DInstanceDesc(sector, boxGeom, whiteMat,
            new World3DTransform(new World3DPosition(8.0, -2.0, 4.0), Quaternion.Identity, new Vector3(2.0f, 1.0f, 4.0f)), CastsShadow: false)), Is.True);
        harness.SubmitFrame(outNoCast, camera);
        byte[] noCastPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outNoCast, noCastPixels);

        int maxShadowDiff = 0;
        for (int i = 0; i < onPixels.Length; i += 4)
        {
            int diff = noCastPixels[i] - onPixels[i];
            if (diff > maxShadowDiff) maxShadowDiff = diff;
        }
        Assert.That(maxShadowDiff, Is.GreaterThan(80), "Shadowed ground must brighten dramatically when CastsShadow=false.");

        // Replace caster with a half-masked custom ISurface quad above ground (UV.x < 0.5 opaque, >= 0.5 discarded).
        harness.System.Shaders.Compiler.RegisterVirtualModule(MaskSurfaceModule, MaskSurfaceModule + ".slang", MaskSurfaceSource);
        Assert.That(scene.TryRemoveInstance(caster), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_quadMin, s_quadMax, out WorldGeometryHandle panelGeom), Is.True);
        // Horizontal panel oriented facing +Z at Z=4 with UV spanning X in [-1, 1] -> [0, 1].
        VertexPBR[] horizMaskVerts =
        [
            new(new Vector3(-4f, -4f, 0f), Vector3.UnitZ, new Vector2(0f, 0f)),
            new(new Vector3(4f, -4f, 0f), Vector3.UnitZ, new Vector2(1f, 0f)),
            new(new Vector3(4f, 4f, 0f), Vector3.UnitZ, new Vector2(1f, 1f)),
            new(new Vector3(-4f, 4f, 0f), Vector3.UnitZ, new Vector2(0f, 1f)),
        ];
        Assert.That(scene.TryRegisterGeometry(new Vector3(-4, -4, 0), new Vector3(4, 4, 0), out WorldGeometryHandle horizGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(horizGeom, 0, horizMaskVerts, GroundIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc
        {
            BaseColor = Vector4.One,
            AlphaMode = World3DAlphaMode.Mask,
            AlphaCutoff = 0.5f,
            DoubleSided = true,
            SurfaceModule = MaskSurfaceModule,
        }, out WorldMaterialHandle maskMat), Is.True);
        // Place high enough out of direct camera view (+Z=12, sun directly down + slightly -Y so left side x<0 casts shadow at x<0, right side x>=0 stays lit).
        scene.Lighting = scene.Lighting with { DirectionToSun = Vector3.Normalize(new Vector3(0f, -0.05f, 1f)) };
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, horizGeom, maskMat, TransformAt(10.0, 0.0, 12.0), CastsShadow: true), out _), Is.True);

        scene.LightingDebugView = World3DLightingDebugView.ShadowFactor;
        harness.SubmitFrame(outMasked, camera);
        byte[] maskPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outMasked, maskPixels);
        bool hasDarkShadow = false;
        bool hasLitGround = false;
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                byte r = maskPixels[(y * size + x) * 4];
                if (r < 25) hasDarkShadow = true;
                if (r > 230) hasLitGround = true;
            }
        }
        Assert.That(hasDarkShadow && hasLitGround, Is.True, "Custom ISurface alpha mask casts both opaque shadow cutout and lit pass-through.");
    }

    /// <summary>Verifies far-cascade small-object radius filtering and LOD bias selection.</summary>
    [Test]
    public void VulkanGpuFarCascadeSmallObjectFilterAndLodBias()
    {
        const int size = 96;
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture outFiltered = harness.CreateOutput(size, size);
        using Texture outUnfiltered = harness.CreateOutput(size, size);
        World3DScene scene = harness.Scene;
        scene.Occlusion.Enabled = false;
        scene.Shadows.Enabled = true;
        scene.Shadows.CascadeResolution = 256;
        scene.Shadows.MaxDistance = 60f;
        scene.Shadows.CascadeSplitLambda = 0.5f;
        scene.Shadows.UseMinMaxAcceleration = false;
        scene.Shadows.FarField.Enabled = false;
        scene.Shadows.FarField.EnableStaticFarCascade = false;

        Vector3 sunDir = Vector3.Normalize(new Vector3(0f, -0.25f, 0.96f));
        scene.Lighting = scene.Lighting with { DirectionToSun = sunDir };
        World3DCamera camera = new(new World3DPosition(0.0, 0.0, 10.0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.35f), MathF.PI / 3f, 0.25f);

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_groundMin, s_groundMax, out WorldGeometryHandle groundGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(groundGeom, 0, s_groundVertices, GroundIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = Vector4.One }, out WorldMaterialHandle whiteMat), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, groundGeom, whiteMat, TransformAt(0, 0, 0), CastsShadow: false), out _), Is.True);

        // Small object placed far out (~40m -> Cascade 2/3) with small radius (< 2m).
        Assert.That(scene.TryRegisterGeometry(s_boxMin, s_boxMax, out WorldGeometryHandle smallGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(smallGeom, 0, s_boxVertices, s_boxIndices, minimumDistance: 0f), Is.True);
        Assert.That(scene.TryQueueGeometryLod(smallGeom, 1, s_boxVertices, s_boxIndices, minimumDistance: 15f), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, smallGeom, whiteMat,
            new World3DTransform(new World3DPosition(38.0, 0.0, 2.0), Quaternion.Identity, new Vector3(1.0f, 1.0f, 2.0f)), CastsShadow: true), out _), Is.True);

        scene.LightingDebugView = World3DLightingDebugView.ShadowFactor;
        // Filter out all objects smaller than radius 10m in cascades 1..3.
        scene.Shadows.CascadeMinObjectRadius = [0f, 10f, 10f, 10f];
        harness.SubmitFrame(outFiltered, camera);
        byte[] filteredPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outFiltered, filteredPixels);

        // Allow small object in all cascades with far LOD bias active.
        scene.Shadows.CascadeMinObjectRadius = [0f, 0f, 0f, 0f];
        scene.Shadows.MinTexelSizeLodBiasMeters = 0.01f;
        harness.SubmitFrame(outUnfiltered, camera);
        byte[] unfilteredPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outUnfiltered, unfilteredPixels);

        int shadowGain = 0;
        for (int i = 0; i < filteredPixels.Length; i += 4)
            shadowGain = Math.Max(shadowGain, filteredPixels[i] - unfilteredPixels[i]);
        Assert.That(shadowGain, Is.GreaterThan(100), "Small far caster is skipped by CascadeMinObjectRadius and casts shadow when allowed.");
    }

    /// <summary>Verifies explicit FarHeightfield streaming, AutoCapture, StaticFar cascade toggle, and optional RSM flux target.</summary>
    [Test]
    public void VulkanGpuFarHeightfieldStaticFarAndOptionalRsm()
    {
        const int size = 96;
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture outHorizon = harness.CreateOutput(size, size);
        using Texture outPatched = harness.CreateOutput(size, size);
        using Texture outRsm = harness.CreateOutput(size, size);
        World3DScene scene = harness.Scene;
        scene.Occlusion.Enabled = false;
        scene.Shadows.Enabled = true;
        scene.Shadows.CascadeResolution = 256;
        scene.Shadows.MaxDistance = 30f;
        scene.Shadows.UseMinMaxAcceleration = false;
        scene.Shadows.FarField.Enabled = true;
        scene.Shadows.FarField.AutoCaptureFromScene = false;
        scene.Shadows.FarField.EnableStaticFarCascade = true;
        scene.Shadows.FarField.StaticFarDistance = 200f;
        scene.Shadows.FarField.WorldCoverageMeters = 4096f;
        scene.Shadows.FarField.HorizonMaxDistance = 2500f;

        // Low sun coming from +X (+5.7 deg elevation) so a tall 600m ridge at X=+800m casts a long shadow over X in [0..400].
        Vector3 sunDir = Vector3.Normalize(new Vector3(1f, 0f, 0.1f));
        scene.Lighting = scene.Lighting with { DirectionToSun = sunDir };
        World3DCamera camera = new(new World3DPosition(0.0, 0.0, 20.0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f), MathF.PI / 3f, 0.5f);

        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);
        Assert.That(scene.TryRegisterGeometry(s_groundMin, s_groundMax, out WorldGeometryHandle groundGeom), Is.True);
        Assert.That(scene.TryQueueGeometryLod(groundGeom, 0, s_groundVertices, GroundIndices), Is.True);
        Assert.That(scene.TryRegisterMaterial(new World3DMaterialDesc { BaseColor = new Vector4(0.8f, 0.7f, 0.6f, 1f) }, out WorldMaterialHandle groundMat), Is.True);
        Assert.That(scene.TryAddInstance(new World3DInstanceDesc(sector, groundGeom, groundMat, TransformAt(250.0, 0.0, 0.0), CastsShadow: true), out _), Is.True);

        // Stream 64x64 far heightfield with a high ridge around texels X in [42..46] (world +640..+896m).
        const int hfSize = 64;
        float[] heights = new float[hfSize * hfSize];
        Array.Fill(heights, -1e6f);
        for (int y = 0; y < hfSize; ++y)
            for (int x = 42; x <= 46; ++x)
                heights[y * hfSize + x] = 600f;

        Assert.That(scene.TrySetFarHeightfield(hfSize, hfSize, 4096f, heights, new World3DCell(0, 0, 0)), Is.True);
        scene.LightingDebugView = World3DLightingDebugView.ShadowFactor;
        harness.SubmitFrame(outHorizon, camera);
        Assert.That(scene.LastRecordedShadows.FarHeightfieldActive, Is.True);
        Assert.That(scene.LastRecordedShadows.StaticFarCascadeActive, Is.True);

        byte[] horizonPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outHorizon, horizonPixels);

        // Flatten the center band of the ridge via TryUpdateFarHeightfieldRegion so center horizon becomes unblocked.
        float[] patch = new float[16 * 16];
        Array.Fill(patch, -1e6f);
        Assert.That(scene.TryUpdateFarHeightfieldRegion(38, 24, 16, 16, patch), Is.True);
        scene.Shadows.FarField.EnableStaticFarCascade = false;
        harness.SubmitFrame(outPatched, camera);
        Assert.That(scene.LastRecordedShadows.StaticFarCascadeActive, Is.False);

        byte[] patchedPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outPatched, patchedPixels);

        int brightenDiff = 0;
        for (int i = 0; i < horizonPixels.Length; i += 4)
            brightenDiff = Math.Max(brightenDiff, patchedPixels[i] - horizonPixels[i]);
        Assert.That(brightenDiff, Is.GreaterThan(100), "Lowering the explicit far height ridge clears its horizon shadow.");

        // Also test AutoCaptureFromScene and optional RSM Flux view.
        scene.ClearFarHeightfield();
        scene.Shadows.FarField.AutoCaptureFromScene = true;
        scene.Shadows.FarField.Resolution = 128;
        scene.Shadows.Rsm.Enabled = true;
        scene.Shadows.Rsm.Resolution = 128;
        scene.LightingDebugView = World3DLightingDebugView.RsmFlux;
        harness.SubmitFrame(outRsm, camera);
        Assert.That(scene.LastRecordedShadows.RsmUpdated, Is.True);

        byte[] rsmPixels = new byte[size * size * 4];
        harness.System.ReadTexture(outRsm, rsmPixels);
        byte maxRsm = 0;
        for (int i = 0; i < rsmPixels.Length; i += 4)
            maxRsm = Math.Max(maxRsm, Math.Max(rsmPixels[i], Math.Max(rsmPixels[i + 1], rsmPixels[i + 2])));
        Assert.That(maxRsm, Is.GreaterThan(10), "Optional RSM flux target produces non-zero bounce radiance.");
    }

    private static World3DTransform TransformAt(double x, double y, double z, float scale = 1f) =>
        new(new World3DPosition(x, y, z), Quaternion.Identity, new Vector3(scale));

    private static void AssertMaxPixelDiff(byte[] actual, byte[] expected, int tolerance, string message)
    {
        int worst = 0;
        for (int i = 0; i < actual.Length; ++i)
        {
            int d = Math.Abs(actual[i] - expected[i]);
            if (d > worst) worst = d;
        }
        Assert.That(worst, Is.LessThanOrEqualTo(tolerance), message);
    }

    private static VertexPBR[] CreateBoxVertices() =>
    [
        // Simple 8-corner unit cube duplicated into face vertices, or 8 shared vertices with smooth normals.
        new(new Vector3(-1, -1, -1), Vector3.Normalize(new Vector3(-1, -1, -1)), Vector2.Zero),
        new(new Vector3(1, -1, -1), Vector3.Normalize(new Vector3(1, -1, -1)), Vector2.UnitX),
        new(new Vector3(1, 1, -1), Vector3.Normalize(new Vector3(1, 1, -1)), Vector2.One),
        new(new Vector3(-1, 1, -1), Vector3.Normalize(new Vector3(-1, 1, -1)), Vector2.UnitY),
        new(new Vector3(-1, -1, 1), Vector3.Normalize(new Vector3(-1, -1, 1)), Vector2.Zero),
        new(new Vector3(1, -1, 1), Vector3.Normalize(new Vector3(1, -1, 1)), Vector2.UnitX),
        new(new Vector3(1, 1, 1), Vector3.Normalize(new Vector3(1, 1, 1)), Vector2.One),
        new(new Vector3(-1, 1, 1), Vector3.Normalize(new Vector3(-1, 1, 1)), Vector2.UnitY),
    ];

    private static uint[] CreateBoxIndices() =>
    [
        // Bottom (-Z), Top (+Z), Front (+X), Back (-X), Right (+Y), Left (-Y) (clockwise outward in Alco LH).
        0, 2, 1, 0, 3, 2,
        4, 5, 6, 4, 6, 7,
        1, 2, 6, 1, 6, 5,
        0, 4, 7, 0, 7, 3,
        2, 3, 7, 2, 7, 6,
        0, 1, 5, 0, 5, 4,
    ];

    private sealed class SceneHarness : IDisposable
    {
        private readonly GPUDevice _device;

        public SceneHarness(GraphicsBackend backend, int textureHeapMb = 64)
        {
            _device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true });
            RenderingSystem? system = null;
            RenderContext? context = null;
            try
            {
                ulong texHeap = backend == GraphicsBackend.NoGPU ? 2ul * 1024 * 1024 : (ulong)textureHeapMb * 1024 * 1024;
                System = system = new RenderingSystem(_device, textureHeapByteCount: texHeap,
                    bufferHeapByteCount: 8 * 1024 * 1024, uploadStagingByteCount: 256 * 1024);
                Context = context = new RenderContext(System, frameDataByteCount: 2 * 1024 * 1024);
                Scene = new World3DScene(System);
                Scene.Shadows.Rsm.Enabled = false;
                Scene.VoxelGi.Enabled = false;
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
            Assert.That(Scene.TryRecordFrame(Context, camera, output.Extent is { X: var x, Y: var y } ? new Extent2D(x, y) : default,
                out World3DView view, target, RGUsage.TransferRead), Is.True);
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
