using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>Exercises the World3D Phase 1 graph, indirect geometry, and clustered direct lighting.</summary>
[TestFixture]
public sealed class TestWorld3DPhase1
{
    private const int FrameSize = 96;
    private const int CommandsPerChunk = 128;
    private const int GeometryPageBytes = 4 * 1024 * 1024;
    private const int IndexedArgsBytes = 5 * sizeof(uint);
    // GBuffer, deferred lighting, tonemap, FXAA, and bloom's pyramid, blur and composite.
    private const int FullGraphWithoutDraws = 4 + 3 * RGNode_Bloom.StageCount + 1;
    private const int FullGraphWithDraws = FullGraphWithoutDraws + 2; // CullSelect and BuildArgs.
    private const string SurfaceModule = "TestPhase1Surface";
    private const string SurfaceSource = """
        #language slang 2025
        module TestPhase1Surface;
        import World3DSurface;

        public struct TestPhase1Surface : ISurface
        {
            public override float4 getBaseColor(SurfaceInput input) { return input.Material.BaseColor.bgra; }
        };
        """;

    private static readonly World3DCamera s_camera = new(default, Quaternion.Identity, MathF.PI / 2, 0.125f);
    private static readonly Vector3 s_panelMin = new(0, -0.75f, -0.75f);
    private static readonly Vector3 s_panelMax = new(0, 0.75f, 0.75f);
    private static readonly VertexPBR[] s_panelVertices =
    [
        new(s_panelMin, -Vector3.UnitX, new Vector2(0, 1)),
        new(new Vector3(0, -0.75f, 0.75f), -Vector3.UnitX, Vector2.Zero),
        new(s_panelMax, -Vector3.UnitX, new Vector2(1, 0)),
        new(new Vector3(0, 0.75f, -0.75f), -Vector3.UnitX, Vector2.One),
    ];

    // Clockwise viewed from the camera (+X forward); the one-sided route must preserve this winding.
    private static ReadOnlySpan<uint> PanelIndices => [0u, 1u, 2u, 0u, 2u, 3u];
    private static ReadOnlySpan<uint> BackfaceIndices => [0u, 2u, 1u, 0u, 3u, 2u];

    /// <summary>Compiles the Phase 1 kernels and standard and custom GBuffer variants without a GPU.</summary>
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Dxil)]
    [TestCase(ShaderTarget.MetalSource)]
    public void ValidateShader_CompilesPhase1KernelsAndSurfaces(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
            Assert.Ignore($"The bundled shader compiler cannot emit {target} on this host.");

        using ShaderCompiler compiler = new(target, new ShaderCompilerOptions { SearchPaths = [] });
        ReadOnlySpan<string> modules = ["World3DSceneData", "World3DOcclusionCommon", "World3DShadowSample",
            "World3DLightClusterCommon", "World3DPointShadowSample", "World3DCullSelect", "World3DBuildArgs",
            "World3DAtmosphere", "World3DDeferredLighting", "World3DSurface", "World3DPbrStandard", "World3DGBuffer"];
        foreach (string module in modules)
        {
            string file = module + ".slang";
            using StreamReader source = new(typeof(World3DScene).Assembly.GetManifestResourceStream(file)
                ?? throw new InvalidOperationException($"Missing embedded shader {file}."));
            compiler.RegisterVirtualModule(module, file, source.ReadToEnd());
        }
        compiler.RegisterVirtualModule(SurfaceModule, SurfaceModule + ".slang", SurfaceSource);

        ReadOnlySpan<(string Module, uint RootBytes)> kernels =
            [("World3DCullSelect", 100), ("World3DBuildArgs", 72), ("World3DDeferredLighting", 176)];
        foreach ((string module, uint bytes) in kernels)
            Assert.That(compiler.Compile(module, ShaderPipelineKind.Compute).Reflection.Root?.SizeBytes, Is.EqualTo(bytes), module);

        ReadOnlySpan<string> surfaces = ["World3DPbrStandard", SurfaceModule];
        ReadOnlySpan<string> specializations = ["false", "true"];
        foreach (string surface in surfaces)
        {
            foreach (string alphaTest in specializations)
            {
                CompiledShader shader = compiler.CompileComposed("World3DGBuffer", surface, ShaderPipelineKind.Graphics,
                    new ShaderCompileOptions { SpecializationArgs = [alphaTest] });
                Assert.That(shader.Reflection.Root?.SizeBytes, Is.EqualTo(40u), $"{surface}, alphaTest={alphaTest}");
            }
        }
    }

    /// <summary>Checks that the full graph follows candidate availability and retries discarded uploads.</summary>
    [Test]
    public void NoGpuFullGraphTracksCandidatesAndRetriesDiscardedUploads()
    {
        using SceneHarness harness = new(GraphicsBackend.NoGPU);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        harness.SubmitFrame(output);
        AssertGraphPasses(scene, FullGraphWithoutDraws);
        AssertNoDraws(scene);

        World3DInstanceDesc instance = CreatePanelDescription(scene);
        AddInstance(scene, instance);
        harness.RecordFrame(output);
        World3DSceneSyncStats pendingSync = scene.LastRecordedSync;
        int pendingCandidateWrites = scene.LastCandidateWrites;
        ulong panelBytes = (ulong)(s_panelVertices.Length * VertexPBR.SizeInBytes + PanelIndices.Length * sizeof(uint));
        Assert.That(pendingSync, Is.EqualTo(new World3DSceneSyncStats(panelBytes, 1, 1, 1, 3)));
        Assert.That(pendingCandidateWrites, Is.Positive);
        Assert.That(scene.LastRecordedDraws, Is.EqualTo(new World3DDrawStats(1, 1)));
        AssertGraphPasses(scene, FullGraphWithDraws + 1); // Scene sync copy.
        Assert.That(scene.IsGeometryLodResident(instance.Geometry, 0), Is.False);
        harness.Context.DiscardFrame();

        // Without completing the retried upload, the discarded LOD must not contribute any draws.
        scene.GeometryUploadBudgetBytes = 0;
        harness.RecordFrame(output);
        Assert.That(scene.IsGeometryLodResident(instance.Geometry, 0), Is.False);
        AssertNoDraws(scene);
        AssertGraphPasses(scene, FullGraphWithoutDraws + 1);
        harness.Context.DiscardFrame();

        scene.GeometryUploadBudgetBytes = panelBytes;
        harness.SubmitFrame(output);
        Assert.That(scene.LastRecordedSync, Is.EqualTo(pendingSync));
        Assert.That(scene.LastCandidateWrites, Is.EqualTo(pendingCandidateWrites));
        Assert.That(scene.LastRecordedDraws, Is.EqualTo(new World3DDrawStats(1, 1)));
        AssertGraphPasses(scene, FullGraphWithDraws + 1);
        Assert.That(scene.IsGeometryLodResident(instance.Geometry, 0), Is.True);

        harness.SubmitFrame(output);
        AssertGraphPasses(scene, FullGraphWithDraws);
        Assert.That(scene.LastCandidateWrites, Is.Zero);
        Assert.That(scene.LastRecordedSync, Is.EqualTo(default(World3DSceneSyncStats)));

        Assert.That(scene.TryUpdateMaterial(instance.Material, new World3DMaterialDesc { AlphaMode = World3DAlphaMode.Blend }), Is.True);
        harness.SubmitFrame(output);
        Assert.That(scene.LastCandidateWrites, Is.EqualTo(1), "Removing the candidate writes its tombstone.");
        AssertNoDraws(scene);
        AssertGraphPasses(scene, FullGraphWithoutDraws + 2); // Candidate copy and material scatter.

        harness.SubmitFrame();
        AssertGraphPasses(scene, 0);
        AssertNoDraws(scene);
    }

    /// <summary>Checks that changes to row data or to the camera anchor do not rewrite draw candidates.</summary>
    [Test]
    public void NoGpuTransformMaterialFactorsAndRebaseLeaveCandidatesAlone()
    {
        using SceneHarness harness = new(GraphicsBackend.NoGPU);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        World3DInstanceDesc instance = CreatePanelDescription(scene);
        WorldInstanceHandle handle = AddInstance(scene, instance);
        Assert.That(harness.SubmitFrame(output).OriginAnchorCell, Is.EqualTo(new World3DCell(0, 0, 0)));
        Assert.That(scene.LastCandidateWrites, Is.Positive);

        World3DTransform transform = new(new(5, 0.5, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.25f), new Vector3(1, 2, 1));
        Assert.That(scene.TryUpdateInstance(handle, instance with { Transform = transform }), Is.True);
        harness.SubmitFrame(output);
        AssertUnchangedCandidates(new World3DSceneSyncStats(0, 0, 0, 1, 0));

        Assert.That(scene.TryUpdateMaterial(instance.Material, new World3DMaterialDesc { Roughness = 0.25f, Metallic = 0.5f }), Is.True);
        harness.SubmitFrame(output);
        AssertUnchangedCandidates(new World3DSceneSyncStats(0, 0, 1, 0, 0));

        World3DCamera movedCamera = s_camera with { Eye = new World3DPosition(2 * World3DSceneCoordinates.CellSize, 0, 0) };
        Assert.That(harness.SubmitFrame(output, movedCamera).OriginAnchorCell, Is.EqualTo(new World3DCell(2, 0, 0)));
        AssertUnchangedCandidates(default);

        void AssertUnchangedCandidates(World3DSceneSyncStats expectedSync)
        {
            Assert.That(scene.LastRecordedSync, Is.EqualTo(expectedSync));
            Assert.That(scene.LastCandidateWrites, Is.Zero);
            Assert.That(scene.LastRecordedDraws, Is.EqualTo(new World3DDrawStats(1, 1)));
        }
    }

    /// <summary>Reads back indexed commands and lit pixels across packets, page-separated LODs and surface routes.</summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactIndexedArgsAndPixelsRoundTrip()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        Assert.That(harness.System.ActiveBackend, Is.EqualTo(GraphicsBackend.VulkanCompact));
        // Isolate base color below the bloom threshold, with a black sky for the backface probe.
        scene.Lighting = new World3DLighting
        {
            SunIntensity = 0, SkyExposure = 0, StarIntensity = 0, SunDiscBrightness = 0, AmbientFloor = 6,
        };

        World3DInstanceDesc standard = CreatePanelDescription(scene, new World3DMaterialDesc { BaseColor = new Vector4(1, 0, 0, 1) });
        World3DInstanceDesc hidden = standard with { Transform = TransformAt(-4, 0, 0) };
        for (int i = 0; i < CommandsPerChunk; ++i)
            AddInstance(scene, hidden);
        harness.SubmitFrame(output); // Establish a full first chunk before adding the visible commands.
        int packetLimit = (int)Math.Clamp(harness.System.Caps.MaxIndirectDrawCount, 1u, (uint)CommandsPerChunk);
        int PacketsFor(int count) => (count + packetLimit - 1) / packetLimit;
        Assert.That(scene.LastRecordedDraws, Is.EqualTo(new World3DDrawStats(PacketsFor(CommandsPerChunk), CommandsPerChunk)));

        // These distinct material handles share a PSO and page, but must fetch different rows and textures.
        Assert.That(scene.TryCreateTexture(new TextureDesc(), [0, 255, 255, 255], out var cyan), Is.True);
        WorldMaterialHandle green = RegisterMaterial(scene, new World3DMaterialDesc
        {
            BaseColor = new Vector4(1, 1, 0, 1), BaseColorTexture = cyan,
        });
        AddInstance(scene, standard with { Transform = TransformAt(4, -2, 2) });
        AddInstance(scene, standard with { Material = green, Transform = TransformAt(4, 2, 2) });
        WorldGeometryHandle backface = QueuePanel(scene, BackfaceIndices);
        AddInstance(scene, standard with { Geometry = backface, Transform = TransformAt(4, 0, 0) });

        harness.System.Shaders.Compiler.LoadModuleFromSource(SurfaceModule, SurfaceModule + ".slang", SurfaceSource);
        WorldMaterialHandle custom = RegisterMaterial(scene, new World3DMaterialDesc { BaseColor = new Vector4(1, 0, 0, 1), SurfaceModule = SurfaceModule });
        WorldGeometryHandle lodGeometry = QueuePanel(scene, PanelIndices);
        // Put the reduced LOD on a dedicated page, with its actual triangle vertices past the 4 MiB boundary.
        VertexPBR[] farVertices = new VertexPBR[GeometryPageBytes / VertexPBR.SizeInBytes + 1 + s_panelVertices.Length];
        uint firstFarVertex = (uint)(farVertices.Length - s_panelVertices.Length);
        s_panelVertices.CopyTo(farVertices, (int)firstFarVertex);
        ReadOnlySpan<uint> farIndices = [firstFarVertex, firstFarVertex + 1, firstFarVertex + 3];
        Assert.That(scene.TryQueueGeometryLod(lodGeometry, 1, farVertices, farIndices, minimumDistance: 10), Is.True);
        World3DInstanceDesc near = standard with { Geometry = lodGeometry, Material = custom, Transform = TransformAt(4, -2, -2) };
        AddInstance(scene, near);
        AddInstance(scene, near with { Transform = TransformAt(16, 8, -8, scale: 4) });
        harness.SubmitFrame(output);

        // Three commands extend the standard bucket, and each custom LOD page gets two candidates.
        Assert.That(scene.LastRecordedDraws, Is.EqualTo(new World3DDrawStats(PacketsFor(CommandsPerChunk) + PacketsFor(3) + 2 * PacketsFor(2),
            CommandsPerChunk + 3 + 2 * 2)));
        BufferSlice standardPayload = scene.GetGeometryPayloadGpuSlice(standard.Geometry, 0);
        BufferSlice backfacePayload = scene.GetGeometryPayloadGpuSlice(backface, 0);
        BufferSlice nearPayload = scene.GetGeometryPayloadGpuSlice(lodGeometry, 0);
        BufferSlice farPayload = scene.GetGeometryPayloadGpuSlice(lodGeometry, 1);
        Assert.That(nearPayload.Buffer, Is.SameAs(standardPayload.Buffer));
        Assert.That(backfacePayload.Buffer, Is.SameAs(standardPayload.Buffer));
        Assert.That(farPayload.Buffer, Is.Not.SameAs(nearPayload.Buffer));
        Assert.That(farPayload.SizeBytes, Is.GreaterThan(GeometryPageBytes));

        BufferSlice argsSlice = scene.GetDrawArgsGpuSlice();
        Assert.That(argsSlice.IsEmpty, Is.False);
        Assert.That(argsSlice.SizeBytes % IndexedArgsBytes, Is.Zero);
        IndexedDrawArgs[] args = new IndexedDrawArgs[checked((int)(argsSlice.SizeBytes / IndexedArgsBytes))];
        harness.System.ReadBuffer(argsSlice.Buffer!, MemoryMarshal.AsBytes(args.AsSpan()), argsSlice.ByteOffset);
        Assert.That(args.Length, Is.GreaterThan(scene.LastRecordedDraws.IndirectSlots), "Readback includes allocation holes.");
        Assert.That(args[..CommandsPerChunk], Is.All.EqualTo(default(IndexedDrawArgs)), "The first chunk is outside the frustum.");
        IndexedDrawArgs standardArgs = ExpectedArgs(standardPayload, s_panelVertices.Length, PanelIndices.Length);
        Assert.That(args.Where(arg => arg != default), Is.EquivalentTo(new IndexedDrawArgs[]
        {
            standardArgs, standardArgs,
            ExpectedArgs(backfacePayload, s_panelVertices.Length, BackfaceIndices.Length),
            ExpectedArgs(nearPayload, s_panelVertices.Length, PanelIndices.Length),
            ExpectedArgs(farPayload, farVertices.Length, farIndices.Length),
        }), "Only the selected page for each visible instance emits an enabled command.");

        byte[] pixels = new byte[FrameSize * FrameSize * 4];
        harness.System.ReadTexture(output, pixels);
        AssertPrimary(PixelAt(pixels, 24, 24), 0, "Second chunk, standard red");
        AssertPrimary(PixelAt(pixels, 72, 24), 1, "Second chunk, same PSO with different material and texture");
        AssertPrimary(PixelAt(pixels, 24, 72), 2, "Custom surface, near LOD on the shared page");
        AssertPrimary(PixelAt(pixels, 69, 75), 2, "Custom surface, far LOD inside the reduced triangle");
        byte[] sky = [0, 0, 0, 255];
        Assert.That(PixelAt(pixels, 48, 48).ToArray(), Is.EqualTo(sky),
            "Reversing the legacy winding leaves the sky even though the backface's indirect command is enabled.");

        // Grow the table with a new mask bucket while clearing a slot in its committed prefix.
        Assert.That(scene.TryUpdateMaterial(green, new World3DMaterialDesc { AlphaMode = World3DAlphaMode.Blend }), Is.True);
        WorldMaterialHandle mask = RegisterMaterial(scene, new World3DMaterialDesc
        {
            BaseColor = new Vector4(1, 1, 1, 0), AlphaMode = World3DAlphaMode.Mask, AlphaCutoff = 0.5f,
        });
        AddInstance(scene, standard with { Material = mask, Transform = TransformAt(4, 2, 2) });
        scene.MaximumDrawDistance = 10;
        harness.SubmitFrame(output);
        Assert.That(scene.TryReadVisibleDrawCount(out int visible), Is.True);
        Assert.That(visible, Is.EqualTo(4), "The moved candidate stays cleared and the distant LOD is rejected.");

        harness.System.ReadTexture(output, pixels);
        AssertPrimary(PixelAt(pixels, 24, 24), 0, "The committed prefix preserves the nearby standard draw");
        Assert.That(PixelAt(pixels, 72, 24).ToArray(), Is.EqualTo(sky), "The mask rejects zero alpha.");
        Assert.That(PixelAt(pixels, 69, 75).ToArray(), Is.EqualTo(sky), "The distant LOD stays culled.");
    }

    /// <summary>Verifies that distant lights within one absolute float ULP do not enter the local view query.</summary>
    [Test]
    [Category("NoGPU")]
    public void NoGpuPointLightQueryStaysLocalAtLargeWorldCoordinates()
    {
        using SceneHarness harness = new(GraphicsBackend.NoGPU);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        scene.LocalLights.MaximumViewDepth = 40f;
        scene.LocalLights.MaxViewLights = 2;
        scene.LocalLights.MaxWideViewLights = 1;
        Assert.That(scene.TryAttachSector(out WorldSectorHandle sector), Is.True);

        const double coordinate = 274_877_906_944.0;
        World3DPosition eye = new(coordinate, -coordinate, coordinate);
        World3DCamera camera = s_camera with { Eye = eye };
        AddAt(4, 0, 0.5f);
        AddAt(6, -1, 1f);
        AddAt(0, 0, 2f);
        AddAt(0.25, 0, 2f);
        AddAt(4, 12, 1f); // Inside the broadphase AABB, but outside the view frustum.

        const int distantLights = 48;
        // These leaves share a region with visible lights and round to the eye's Y in absolute floats.
        Assert.That((float)(eye.Y + 1000 + (distantLights - 1) * 100), Is.EqualTo((float)eye.Y));
        for (int i = 0; i < distantLights; ++i)
            AddAt(12, 1000 + i * 100, 1f);

        harness.SubmitFrame(output, camera);
        World3DLocalLightStats stats = scene.LastRecordedLocalLights;
        Assert.That(stats.ResidentLights, Is.EqualTo(distantLights + 5));
        Assert.That(stats.QueriedLights, Is.EqualTo(5), "Only regional leaf overlaps should reach the frustum test.");
        Assert.That(stats.VisibleLights, Is.EqualTo(4));
        Assert.That(stats.SelectedLights, Is.EqualTo(2));
        Assert.That(stats.DroppedLights, Is.EqualTo(2));
        Assert.That(stats.WideLightsDropped, Is.EqualTo(1));

        void AddAt(double x, double y, float radius) =>
            AddPointLight(scene, sector, new(new World3DPosition(eye.X + x, eye.Y + y, eye.Z), Vector3.One, 1f, radius));
    }

    /// <summary>Checks deferred direct lighting from a negative-depth center and from outside the side of the frustum.</summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactClusteredPointLightsReachAcrossTheNearPlaneAndFrustumEdge()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        Assert.That(harness.System.ActiveBackend, Is.EqualTo(GraphicsBackend.VulkanCompact));
        scene.Lighting = UnlitEnvironment();
        World3DInstanceDesc receiver = CreatePanelDescription(scene, new World3DMaterialDesc { Roughness = 0.8f }) with
        {
            Transform = TransformAt(2, 0, 0, scale: 3),
            CastsShadow = false,
        };
        AddInstance(scene, receiver);
        byte[] unlit = CaptureFrame(harness, output);

        // The red center is behind the eye and in an adjacent world cell. The blue center is beyond the
        // right frustum plane; nevertheless both influence spheres reach visible fragments in different clusters.
        AddPointLight(scene, receiver.Sector, new(new World3DPosition(-0.25, -1.4, 0), Vector3.UnitX, 65f, 3.2f)
        {
            AffectsGlobalIllumination = false,
        });
        AddPointLight(scene, receiver.Sector, new(new World3DPosition(1.1, 2, 0), Vector3.UnitZ, 10f, 1.5f)
        {
            AffectsGlobalIllumination = false,
        });
        byte[] lit = CaptureFrame(harness, output);
        Assert.That(scene.LastRecordedLocalLights.SelectedLights, Is.EqualTo(2));

        const int row = FrameSize / 2;
        const int left = 14;
        const int right = FrameSize - 4;
        Vector3 nearPlane = AveragePatchRgb(lit, left, row);
        Vector3 frustumEdge = AveragePatchRgb(lit, right, row);
        Assert.That(nearPlane.X, Is.GreaterThan(AveragePatchRgb(unlit, left, row).X + 50f), "The behind-eye center must still illuminate the receiver.");
        Assert.That(nearPlane.X, Is.GreaterThan(nearPlane.Z + 50f));
        Assert.That(frustumEdge.Z, Is.GreaterThan(AveragePatchRgb(unlit, right, row).Z + 50f), "The edge tile must retain lights with offscreen centers.");
        Assert.That(frustumEdge.Z, Is.GreaterThan(frustumEdge.X + 50f));
    }

    /// <summary>Checks that the forward pass chooses a cluster using the transparent fragment's depth, even over sky.</summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactClusteredPointLightsShadeBlendWithoutOpaqueDepth()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        Assert.That(harness.System.ActiveBackend, Is.EqualTo(GraphicsBackend.VulkanCompact));
        scene.Lighting = UnlitEnvironment();
        scene.Forward.Enabled = true;
        World3DInstanceDesc pane = CreatePanelDescription(scene, new World3DMaterialDesc
        {
            AlphaMode = World3DAlphaMode.Blend,
            BaseColor = new Vector4(1f, 1f, 1f, 0.6f),
            Roughness = 0.8f,
        }) with { CastsShadow = false };
        AddInstance(scene, pane);
        const int center = FrameSize / 2;
        Vector3 unlit = AveragePatchRgb(CaptureFrame(harness, output), center, center);

        AddPointLight(scene, pane.Sector, new(new World3DPosition(2, 0, 0), Vector3.UnitY, 20f, 3.75f)
        {
            AffectsGlobalIllumination = false,
        });
        Vector3 lit = AveragePatchRgb(CaptureFrame(harness, output), center, center);
        Assert.That(scene.LastRecordedDraws.IndirectSlots, Is.Zero, "This frame has no opaque depth producers.");
        Assert.That(scene.LastRecordedForward.CandidateCount, Is.EqualTo(1u));
        Assert.That(lit.Y, Is.GreaterThan(unlit.Y + 50f));
        Assert.That(lit.Y, Is.GreaterThan(MathF.Max(lit.X, lit.Z) + 50f));
    }

    /// <summary>Verifies that view and cluster caps retain higher-scoring lights with stable ordering across redraws.</summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompactClusteredPointLightOverflowRetainsRankedLights()
    {
        using SceneHarness harness = new(GraphicsBackend.VulkanCompact);
        using Texture output = harness.CreateOutput();
        World3DScene scene = harness.Scene;
        Assert.That(harness.System.ActiveBackend, Is.EqualTo(GraphicsBackend.VulkanCompact));
        scene.Lighting = UnlitEnvironment();
        scene.LocalLights.EnableOverflowTelemetry = true;
        const int weakLights = 140;
        const int maxSelected = 136; // Still enough to overflow coarse (128) and fine (32) bins after view selection.
        scene.LocalLights.MaxViewLights = maxSelected;
        World3DInstanceDesc receiver = CreatePanelDescription(scene, new World3DMaterialDesc { Roughness = 0.8f }) with
        {
            Transform = TransformAt(5, 0, 0, scale: 2),
            CastsShadow = false,
        };
        AddInstance(scene, receiver);

        World3DPointLight weak = new(new World3DPosition(4, 0, 0), Vector3.UnitX, 0.01f, 1.5f)
        {
            AffectsGlobalIllumination = false,
        };
        for (int i = 0; i < weakLights; ++i)
            AddPointLight(scene, receiver.Sector, weak);
        // Equal high scores, but both lights were added after enough weak ones to fill the bins.
        AddPointLight(scene, receiver.Sector, weak with { Color = Vector3.UnitY, Intensity = 5f });
        AddPointLight(scene, receiver.Sector, weak with { Color = Vector3.UnitZ, Intensity = 5f });

        const int center = FrameSize / 2;
        Vector3 first = AveragePatchRgb(CaptureFrame(harness, output), center, center);
        Assert.That(scene.LastRecordedLocalLights.SelectedLights, Is.EqualTo(maxSelected));
        Assert.That(scene.LastRecordedLocalLights.DroppedLights, Is.EqualTo(weakLights + 2 - maxSelected));
        Assert.That(first.Y, Is.GreaterThan(first.X + 50f), "The late green light must beat the weak red lights.");
        Assert.That(first.Z, Is.GreaterThan(first.X + 50f), "The late blue light must also survive the cluster caps.");

        // The next recording consumes the asynchronous readback from the already-completed frame.
        Vector3 repeat = AveragePatchRgb(CaptureFrame(harness, output), center, center);
        Assert.That(Vector3.Distance(repeat, first), Is.LessThanOrEqualTo(2f));
        World3DLocalLightClusterStats overflow = scene.LastCompletedLocalLightClusters;
        Assert.That(overflow.FrameId, Is.Positive);
        Assert.That(overflow.CoarseOverflowClusters, Is.Positive);
        Assert.That(overflow.CoarseDroppedLights, Is.Positive);
        Assert.That(overflow.FineOverflowClusters, Is.Positive);
        Assert.That(overflow.FineDroppedLights, Is.Positive);

        scene.LocalLights.MaxViewLights = 1;
        Vector3 tie = AveragePatchRgb(CaptureFrame(harness, output), center, center);
        Assert.That(scene.LastRecordedLocalLights.SelectedLights, Is.EqualTo(1));
        Assert.That(tie.Y, Is.GreaterThan(MathF.Max(tie.X, tie.Z) + 50f), "Equal scores favor the earlier (lower-slot) green light.");
    }

    private static World3DLighting UnlitEnvironment() => new()
    {
        SunIntensity = 0f, SkyExposure = 0f, StarIntensity = 0f, SunDiscBrightness = 0f, AmbientFloor = 0f,
    };

    private static void AddPointLight(World3DScene scene, WorldSectorHandle sector, in World3DPointLight light) =>
        Assert.That(scene.TryAddPointLight(sector, in light, out _), Is.True);

    private static byte[] CaptureFrame(SceneHarness harness, Texture output)
    {
        harness.SubmitFrame(output);
        byte[] pixels = new byte[FrameSize * FrameSize * 4];
        harness.System.ReadTexture(output, pixels);
        return pixels;
    }

    private static Vector3 AveragePatchRgb(byte[] pixels, int x, int y)
    {
        Vector3 sum = Vector3.Zero;
        for (int dy = -1; dy <= 1; ++dy)
        {
            for (int dx = -1; dx <= 1; ++dx)
            {
                ReadOnlySpan<byte> pixel = PixelAt(pixels, x + dx, y + dy);
                sum += new Vector3(pixel[0], pixel[1], pixel[2]);
            }
        }
        return sum / 9f;
    }

    private static World3DInstanceDesc CreatePanelDescription(World3DScene scene, World3DMaterialDesc? material = null)
    {
        Assert.That(scene.TryAttachSector(out var sector), Is.True);
        return new World3DInstanceDesc(sector, QueuePanel(scene, PanelIndices),
            RegisterMaterial(scene, material ?? new World3DMaterialDesc()), TransformAt(4, 0, 0));
    }

    private static WorldGeometryHandle QueuePanel(World3DScene scene, ReadOnlySpan<uint> indices)
    {
        Assert.That(scene.TryRegisterGeometry(s_panelMin, s_panelMax, out var geometry), Is.True);
        Assert.That(scene.TryQueueGeometryLod(geometry, 0, s_panelVertices, indices), Is.True);
        return geometry;
    }

    private static WorldMaterialHandle RegisterMaterial(World3DScene scene, in World3DMaterialDesc description)
    {
        Assert.That(scene.TryRegisterMaterial(description, out var handle), Is.True);
        return handle;
    }

    private static WorldInstanceHandle AddInstance(World3DScene scene, in World3DInstanceDesc description)
    {
        Assert.That(scene.TryAddInstance(description, out var handle), Is.True);
        return handle;
    }

    private static World3DTransform TransformAt(double x, double y, double z, float scale = 1) =>
        new(new World3DPosition(x, y, z), Quaternion.Identity, new Vector3(scale));

    private static void AssertGraphPasses(World3DScene scene, int expected)
    {
        Assert.That(scene.LastGraphStatistics.TotalPassCount, Is.EqualTo(expected));
        Assert.That(scene.LastGraphStatistics.AlivePassCount, Is.EqualTo(expected));
    }

    private static void AssertNoDraws(World3DScene scene)
    {
        Assert.That(scene.LastRecordedDraws, Is.EqualTo(default(World3DDrawStats)));
        Assert.That(scene.GetDrawArgsGpuSlice().IsEmpty, Is.True);
    }

    private static IndexedDrawArgs ExpectedArgs(BufferSlice payload, int vertices, int indices)
    {
        Assert.That(payload.IsEmpty, Is.False, "The selected LOD is resident after submission.");
        return new IndexedDrawArgs((uint)indices, 1, (uint)((payload.ByteOffset + (ulong)vertices * (uint)VertexPBR.SizeInBytes) / sizeof(uint)), 0, 0);
    }

    private static ReadOnlySpan<byte> PixelAt(byte[] pixels, int x, int y) => pixels.AsSpan((y * FrameSize + x) * 4, 4);

    private static void AssertPrimary(ReadOnlySpan<byte> pixel, int channel, string probe)
    {
        Assert.That(pixel[channel], Is.GreaterThan(128), probe);
        Assert.That(pixel[(channel + 1) % 3], Is.LessThan(32), probe);
        Assert.That(pixel[(channel + 2) % 3], Is.LessThan(32), probe);
        Assert.That(pixel[3], Is.EqualTo(255), probe);
    }

    [StructLayout(LayoutKind.Sequential, Size = IndexedArgsBytes)]
    private readonly record struct IndexedDrawArgs(uint IndexCount, uint InstanceCount, uint FirstIndex, int VertexOffset, uint FirstInstance);

    private sealed class SceneHarness : IDisposable
    {
        private readonly GPUDevice _device;

        /// <summary>Creates a headless rendering system and scene.</summary>
        public SceneHarness(GraphicsBackend backend)
        {
            _device = CreateDevice(backend);
            RenderingSystem? system = null;
            RenderContext? context = null;
            try
            {
                System = system = new RenderingSystem(_device, textureHeapByteCount: 2 * 1024 * 1024,
                    bufferHeapByteCount: 2 * 1024 * 1024, uploadStagingByteCount: 64 * 1024);
                Context = context = new RenderContext(System);
                Scene = new World3DScene(System);
                Scene.Occlusion.Enabled = false;
                Scene.Shadows.Enabled = false;
                Scene.VoxelGi.Enabled = false;
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

        /// <summary>The rendering system owning the scene.</summary>
        public RenderingSystem System { get; }
        /// <summary>The render context on the scene's submission stream.</summary>
        public RenderContext Context { get; }
        /// <summary>The tested scene.</summary>
        public World3DScene Scene { get; }

        /// <summary>Creates an sRGB render target that can be read back.</summary>
        public Texture CreateOutput() => System.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(FrameSize, FrameSize, 1),
            Format = Format.Rgba8Srgb,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });

        /// <summary>Records a frame without submitting it.</summary>
        public World3DView RecordFrame(Texture? output, World3DCamera? camera = null)
        {
            Context.BeginFrame();
            RenderGraphTarget target = output == null ? default : RenderGraphTarget.FromTexture(output);
            RGUsage finalUsage = output == null ? RGUsage.None : RGUsage.TransferRead;
            Assert.That(Scene.TryRecordFrame(Context, camera ?? s_camera, new Extent2D(FrameSize, FrameSize), out var view, target, finalUsage), Is.True);
            return view;
        }

        /// <summary>Records and waits for a full or headless frame.</summary>
        public World3DView SubmitFrame(Texture? output = null, World3DCamera? camera = null)
        {
            World3DView view = RecordFrame(output, camera);
            Context.SubmitWithCompletion().Wait();
            return view;
        }

        /// <summary>Discards pending commands and releases the owned rendering resources.</summary>
        public void Dispose()
        {
            Context.DiscardFrame();
            Scene.Dispose();
            Context.Dispose();
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
