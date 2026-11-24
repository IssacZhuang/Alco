using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// CPU RenderGraph planning/aliasing/zero-GC tests and multi-backend GPU mathematical parity tests for <see cref="RGNode_Bloom"/>.
/// </summary>
[TestFixture]
public unsafe class TestBloomNode
{
    private const int StageCount = RGNode_Bloom.StageCount;
    private const float MaxRadius = 31.0f;

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedSetupRoot
    {
        public uint Source;
        public uint Dest;
        public Vector2 InvDestSize;
        public uint DestSizeX;
        public uint DestSizeY;
        public float Threshold;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedDownsampleRoot
    {
        public uint Source;
        public uint Dest;
        public Vector2 InvSourceSize;
        public Vector2 InvDestSize;
        public uint DestSizeX;
        public uint DestSizeY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedGaussianHRoot
    {
        public uint Source;
        public uint Dest;
        public uint SizeX;
        public uint SizeY;
        public float SigmaRadius;
        public int IntegerRadius;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedGaussianVRoot
    {
        public uint Source;
        public uint Additive;
        public uint Dest;
        public Vector2 InvDestSize;
        public uint SizeX;
        public uint SizeY;
        public float TintR;
        public float TintG;
        public float TintB;
        public float SigmaRadius;
        public int IntegerRadius;
        public float AdditiveBlend;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedBlitRoot
    {
        public uint BloomTex;
        public uint SceneTex;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedCompositeRoot
    {
        public uint SceneTex;
        public uint BloomTex;
        public uint Dest;
        public Vector2 InvDestSize;
        public uint DestSizeX;
        public uint DestSizeY;
        public uint HasBloom;
    }

    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void BloomShaders_CompileAndMatchRootLayouts_AcrossAllTargets(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
        {
            Assert.Ignore($"Shader target {target} is unsupported on this host.");
            return;
        }

        using ShaderCompiler compiler = new(target);
        compiler.LoadModuleFromSource(
            "AlcoRendering_Bloom",
            "AlcoRendering_Bloom.slang",
            LoadEmbeddedShader("AlcoRendering_Bloom.slang"));

        VerifyTargetLayout<ExpectedSetupRoot>(compiler, "BloomSetup", "BloomSetupRoot", ShaderPipelineKind.Compute);
        VerifyTargetLayout<ExpectedDownsampleRoot>(compiler, "BloomDownsample", "BloomDownsampleRoot", ShaderPipelineKind.Compute);
        VerifyTargetLayout<ExpectedGaussianHRoot>(compiler, "BloomGaussianH", "BloomGaussianHRoot", ShaderPipelineKind.Compute);
        VerifyTargetLayout<ExpectedGaussianVRoot>(compiler, "BloomGaussianV", "BloomGaussianVRoot", ShaderPipelineKind.Compute);
        VerifyTargetLayout<ExpectedCompositeRoot>(compiler, "BloomComposite", "BloomCompositeRoot", ShaderPipelineKind.Compute);
        VerifyTargetLayout<ExpectedBlitRoot>(compiler, "BloomBlit", "BloomBlitRoot", ShaderPipelineKind.Graphics);
    }

    [Test]
    public void NodeConstruction_SupportsMultipleInstancesOnSharedRenderingSystem()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderChain chainA = new();
        RenderChain chainB = new();

        RGNode_Bloom bloomA = new(host.RenderingSystem, chainA, name: "MainBloom");
        RGNode_Bloom bloomB = new(host.RenderingSystem, chainB, name: "OverworldBloom");

        Assert.That(bloomA.Name, Is.EqualTo("MainBloom"));
        Assert.That(bloomB.Name, Is.EqualTo("OverworldBloom"));
        Assert.That(bloomA.Threshold, Is.EqualTo(0.0f));
        Assert.That(bloomA.Intensity, Is.EqualTo(1.0f));
        Assert.That(bloomA.SizeScale, Is.EqualTo(1.0f));
        Assert.That(bloomA.Sizes, Is.EqualTo(new[] { 0.3f, 1.0f, 2.0f, 10.0f, 30.0f, 64.0f }));
    }

    [Test]
    public void RenderGraph_OddResolutionPyramid_Aliasing_Culling_And_ZeroSteadyStateAllocations()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderContext context = new(host.RenderingSystem);
        RenderGraph graph = new(host.RenderingSystem, new Extent2D(1919, 1079), "BloomNoGpuGraph");
        RenderChain chain = new();
        Texture sceneColor = CreateHdrTexture(host.RenderingSystem, 1919, 1079,
            TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination);

        try
        {
            RGTexture importedScene = graph.ImportTexture(sceneColor, "SceneHDR", finalUsage: RGUsage.PixelRead);
            RGNode_Bloom bloom = new(host.RenderingSystem, chain);

            graph.Use(new LambdaNode(b =>
            {
                chain.Reset(importedScene);
                RenderGraphPassBuilder writePass = b.AddPass("SceneWrite");
                writePass.Write(importedScene, RGUsage.ColorAttachmentWrite);
            }));
            graph.Use(bloom);
            graph.Use(new LambdaNode(b =>
            {
                RenderGraphPassBuilder consumePass = b.AddPass("Consume");
                consumePass.Read(chain.Current, RGUsage.PixelRead);
                consumePass.ProducesOutput();
            }));

            // 1. Full 6-stage active frame: 1 Setup + 5 Downsample + 12 Gaussian + 1 Composite + SceneWrite + Consume = 21 passes.
            ExecuteNoGpuFrame(graph, context);
            RenderGraphStatistics fullStats = graph.Statistics;
            Assert.That(fullStats.TotalPassCount, Is.EqualTo(21));
            Assert.That(fullStats.AlivePassCount, Is.EqualTo(21));
            Assert.That(fullStats.CulledPassCount, Is.Zero);
            // 6 pyramid (L0..L5) + 6 BlurH + 6 Accum = 18 logical transient Bloom textures alias heavily across LIFO pool lifetimes.
            Assert.That(fullStats.LogicalTransientTextureCount, Is.EqualTo(18));
            Assert.That(fullStats.ActivePhysicalTextureCount, Is.LessThan(fullStats.LogicalTransientTextureCount),
                "Bloom pyramid and gaussian transients should alias physical pool textures.");
            TestContext.WriteLine($"Aliasing on 1919x1079: {fullStats.LogicalTransientTextureCount} logical -> {fullStats.ActivePhysicalTextureCount} physical textures");

            // 2. SeparateTargetCompute creates 1 full-res output transient (19 logical total) and advances the chain.
            bloom.CompositeMode = BloomCompositeMode.SeparateTargetCompute;
            RGTexture? capturedOutput = null;
            graph.Use(new LambdaNode(b =>
            {
                capturedOutput = chain.Current;
            }));
            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedOutput, Is.Not.EqualTo(importedScene));
            Assert.That(graph.Statistics.LogicalTransientTextureCount, Is.EqualTo(19));

            // 3. Partial stage culling: disable coarse stages feeding L2..L5 -> Downsample2..5 auto-culled.
            bloom.CompositeMode = BloomCompositeMode.InPlaceAdditive;
            bloom.Sizes[2] = 0.0f;
            bloom.Sizes[3] = 0.0f;
            bloom.Sizes[4] = 0.0f;
            bloom.Sizes[5] = 0.0f;
            ExecuteNoGpuFrame(graph, context);
            // Declared: SceneWrite(1) + Setup(1) + Downsample1..5(5) + Gaussian(2*2=4) + BloomComposite(1) + Consume(1) = 13;
            // Downsample2..5 (4 passes) must be culled cleanly!
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(13));
            Assert.That(graph.Statistics.CulledPassCount, Is.EqualTo(4));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(9));
            ReadOnlySpan<RenderGraphPassReport> reports = graph.PassReports;
            for (int r = 0; r < reports.Length; ++r)
            {
                if (reports[r].Name is "Bloom_Downsample2" or "Bloom_Downsample3" or "Bloom_Downsample4" or "Bloom_Downsample5")
                {
                    Assert.That(reports[r].Alive, Is.False, $"{reports[r].Name} should be culled when L2..L5 have no consumers.");
                }
            }

            // 4. Intensity == 0 disables Bloom passes completely while preserving chain.Current.
            bloom.Intensity = 0.0f;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(2));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(2));
            Assert.That(graph.Statistics.LogicalTransientTextureCount, Is.Zero);

            // 5. Re-enable all stages and verify zero managed heap allocations across steady-state frames.
            bloom.Intensity = 1.0f;
            bloom.Sizes[2] = 2.0f;
            bloom.Sizes[3] = 10.0f;
            bloom.Sizes[4] = 30.0f;
            bloom.Sizes[5] = 64.0f;
            for (int i = 0; i < 4; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            Assert.That(allocatedBytes, Is.Zero,
                $"Steady-state Bloom RenderGraph Setup+Execute must allocate 0 managed bytes (allocated={allocatedBytes}).");
        }
        finally
        {
            graph.Dispose();
            if (!sceneColor.IsDisposed)
            {
                sceneColor.Dispose();
            }
            context.Dispose();
        }
    }

    [TestCase(GraphicsBackend.NoGPU)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void Gpu_OddDimensionsThresholdNaNsAndMultiStageBlur_MatchOriginalCpuGoldenReference(GraphicsBackend backend)
    {
        if (backend == GraphicsBackend.Vulkan && !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Ignore("Full Vulkan backend is tested on Windows/Linux.");
            return;
        }

        GPUDevice device;
        try
        {
            device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true });
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"Backend {backend} is unsupported on this host: {ex.Message}");
            return;
        }

        RenderingSystem rendering = new(device, 32 * 1024 * 1024);
        RenderContext context = new(rendering);
        try
        {
            // Odd dimensions 129x65 exercise asymmetric >> 1 mip scaling down to 2x1,
            // border clamping, threshold ramp, negative clamping, NaN/Inf sanitization,
            // skipped stage, stage tints, and alpha preservation.
            const uint width = 129;
            const uint height = 65;
            Vector4[] hdrInput = BuildTestHdrImage((int)width, (int)height);

            float threshold = 0.5f;
            float intensity = 1.6f;
            float sizeScale = 1.25f;
            float[] sizes = [0.3f, 1.0f, 2.0f, 0.0f, 30.0f, 64.0f];
            Vector3[] tints =
            [
                new Vector3(1.0f, 0.9f, 0.8f),
                new Vector3(0.8f, 1.0f, 0.9f),
                new Vector3(0.9f, 0.8f, 1.0f),
                Vector3.One,
                new Vector3(1.2f, 0.7f, 0.5f),
                new Vector3(0.5f, 0.9f, 1.3f),
            ];

            if (backend == GraphicsBackend.NoGPU)
            {
                // Verify all three composite modes record without errors on NoGPU.
                VerifyCompositeModeRecordsCleanly(rendering, context, hdrInput, width, height, threshold, intensity, sizeScale, sizes, tints, BloomCompositeMode.InPlaceAdditive, true);
                VerifyCompositeModeRecordsCleanly(rendering, context, hdrInput, width, height, threshold, intensity, sizeScale, sizes, tints, BloomCompositeMode.SeparateTargetCompute, true);
                VerifyCompositeModeRecordsCleanly(rendering, context, hdrInput, width, height, threshold, intensity, sizeScale, sizes, tints, BloomCompositeMode.SeparateTargetGraphics, true);
                return;
            }

            Vector4[] golden = ComputeOriginalBloomGoldenReference(hdrInput, (int)width, (int)height, threshold, intensity, sizeScale, sizes, tints);

            Vector4[] gpuInPlace = ExecuteGpuBloom(
                rendering, context, hdrInput, width, height, threshold, intensity, sizeScale, sizes, tints,
                BloomCompositeMode.InPlaceAdditive, separateTarget: false);
            AssertImagesMatch(golden, gpuInPlace, hdrInput, (int)width, (int)height, maxAbsTol: 0.04f, relTol: 0.015f, "InPlaceAdditive");

            Vector4[] gpuSeparateCompute = ExecuteGpuBloom(
                rendering, context, hdrInput, width, height, threshold, intensity, sizeScale, sizes, tints,
                BloomCompositeMode.SeparateTargetCompute, separateTarget: true);
            AssertImagesMatch(golden, gpuSeparateCompute, hdrInput, (int)width, (int)height, maxAbsTol: 0.04f, relTol: 0.015f, "SeparateTargetCompute");

            Vector4[] gpuSeparateGraphics = ExecuteGpuBloom(
                rendering, context, hdrInput, width, height, threshold, intensity, sizeScale, sizes, tints,
                BloomCompositeMode.SeparateTargetGraphics, separateTarget: true);
            AssertImagesMatch(golden, gpuSeparateGraphics, hdrInput, (int)width, (int)height, maxAbsTol: 0.04f, relTol: 0.015f, "SeparateTargetGraphics");

            // Large radius + multi-tile LDS verification (256x128 hits radius=31 clamping and spans multiple 64x1 / 8x32 threadgroups).
            const uint largeW = 256;
            const uint largeH = 128;
            Vector4[] largeInput = BuildMultiTileHdrImage((int)largeW, (int)largeH);
            float[] largeSizes = [5.0f, 12.0f, 25.0f, 40.0f, 60.0f, 90.0f];
            Vector3[] largeTints = [Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One, Vector3.One];
            Vector4[] largeGolden = ComputeOriginalBloomGoldenReference(largeInput, (int)largeW, (int)largeH, 0.0f, 1.0f, 3.0f, largeSizes, largeTints);
            Vector4[] largeGpu = ExecuteGpuBloom(
                rendering, context, largeInput, largeW, largeH, 0.0f, 1.0f, 3.0f, largeSizes, largeTints,
                BloomCompositeMode.SeparateTargetCompute, separateTarget: true);
            AssertImagesMatch(largeGolden, largeGpu, largeInput, (int)largeW, (int)largeH, maxAbsTol: 0.05f, relTol: 0.015f, "MultiTileMaxRadius");

            // Negative threshold disables cutoff; tiny 3x2 image clamps all pyramid mips to 1x1 safely.
            const uint tinyW = 3;
            const uint tinyH = 2;
            Vector4[] tinyInput =
            [
                new Vector4(0.1f, 0.2f, 0.3f, 0.4f), new Vector4(0.4f, 0.2f, 0.1f, 0.8f), new Vector4(0.2f, 0.5f, 0.1f, 1.0f),
                new Vector4(0.3f, 0.1f, 0.4f, 0.6f), new Vector4(0.5f, 0.3f, 0.2f, 0.9f), new Vector4(0.1f, 0.4f, 0.2f, 0.2f),
            ];
            float[] allOnSizes = [0.3f, 1.0f, 2.0f, 10.0f, 30.0f, 64.0f];
            Vector4[] tinyGolden = ComputeOriginalBloomGoldenReference(tinyInput, (int)tinyW, (int)tinyH, -1.0f, 1.0f, 1.0f, allOnSizes, largeTints);
            Vector4[] tinyGpu = ExecuteGpuBloom(
                rendering, context, tinyInput, tinyW, tinyH, -1.0f, 1.0f, 1.0f, allOnSizes, largeTints,
                BloomCompositeMode.InPlaceAdditive, separateTarget: false);
            AssertImagesMatch(tinyGolden, tinyGpu, tinyInput, (int)tinyW, (int)tinyH, maxAbsTol: 0.01f, relTol: 0.01f, "TinyNegativeThreshold");

            // Intensity == 0 writing to builder.Output copies scene cleanly without running bloom passes.
            VerifyZeroIntensityPassthroughToOutput(rendering, context, hdrInput, width, height);
        }
        finally
        {
            device.WaitIdle();
            context.Dispose();
            rendering.Dispose();
            device.Destroy();
        }
    }

    private static void VerifyTargetLayout<TRoot>(
        ShaderCompiler compiler,
        string moduleName,
        string rootStructName,
        ShaderPipelineKind kind)
        where TRoot : unmanaged
    {
        CompiledShader compiled = compiler.CompileSource(
            moduleName,
            moduleName + ".slang",
            LoadEmbeddedShader(moduleName + ".slang"),
            kind);

        Assert.That(compiled.Reflection.Root, Is.Not.Null, $"{moduleName} must reflect a [GPURoot] layout on {compiler.Target}.");
        Assert.That(compiled.Reflection.Root!.SizeBytes, Is.EqualTo((uint)sizeof(TRoot)),
            $"{moduleName} root byte size mismatch on {compiler.Target}.");
        Assert.That(compiled.Reflection.MatchesStructLayout<TRoot>(rootStructName), Is.True,
            $"{moduleName} root field layout mismatch with {typeof(TRoot).Name} on {compiler.Target}.");
    }

    private static void ExecuteNoGpuFrame(RenderGraph graph, RenderContext context)
    {
        context.BeginFrame();
        graph.Execute(context);
        context.Submit();
    }

    private static void VerifyCompositeModeRecordsCleanly(
        RenderingSystem rendering,
        RenderContext context,
        Vector4[] input,
        uint width,
        uint height,
        float threshold,
        float intensity,
        float sizeScale,
        float[] sizes,
        Vector3[] tints,
        BloomCompositeMode mode,
        bool isNoGpu)
    {
        _ = ExecuteGpuBloom(rendering, context, input, width, height, threshold, intensity, sizeScale, sizes, tints, mode,
            mode != BloomCompositeMode.InPlaceAdditive, readback: !isNoGpu);
    }

    private static void VerifyZeroIntensityPassthroughToOutput(
        RenderingSystem rendering,
        RenderContext context,
        Vector4[] hdrInput,
        uint width,
        uint height)
    {
        Texture inputTex = CreateHdrTexture(
            rendering,
            width,
            height,
            TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.TransferDestination);
        Texture outputTex = CreateHdrTexture(
            rendering,
            width,
            height,
            TextureUsage.ColorAttachment | TextureUsage.TransferSource);
        RenderGraph graph = new(rendering, new Extent2D(width, height), "BloomOutputPassthrough");
        try
        {
            UploadRgba16Float(rendering, inputTex, hdrInput);
            RGNode_Bloom bloom = new(
                rendering,
                b => b.ImportTexture(inputTex, "InputPassthrough"),
                b => b.Output)
            {
                Intensity = 0.0f,
                CompositeMode = BloomCompositeMode.SeparateTargetCompute, // Must auto-downgrade safely for builder.Output.
            };
            graph.Use(bloom);

            context.BeginFrame();
            graph.Execute(context, RenderGraphTarget.FromTexture(outputTex), outputFinalUsage: RGUsage.TransferRead);
            context.Submit();
            rendering.WaitIdle();

            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(1));
            Vector4[] actual = ReadbackRgba16Float(rendering, outputTex, (int)width, (int)height);
            Vector4[] expected = new Vector4[hdrInput.Length];
            for (int i = 0; i < expected.Length; ++i)
            {
                expected[i] = QuantizeHalf(hdrInput[i]);
            }
            AssertImagesMatch(expected, actual, hdrInput, (int)width, (int)height, maxAbsTol: 0.002f, relTol: 0.002f, "ZeroIntensityOutputPassthrough");
        }
        finally
        {
            graph.Dispose();
            if (!outputTex.IsDisposed)
            {
                outputTex.Dispose();
            }
            if (!inputTex.IsDisposed)
            {
                inputTex.Dispose();
            }
        }
    }

    private static Vector4[] ExecuteGpuBloom(
        RenderingSystem rendering,
        RenderContext context,
        Vector4[] hdrInput,
        uint width,
        uint height,
        float threshold,
        float intensity,
        float sizeScale,
        float[] sizes,
        Vector3[] tints,
        BloomCompositeMode mode,
        bool separateTarget,
        bool readback = true)
    {
        Texture inputTex = CreateHdrTexture(
            rendering,
            width,
            height,
            TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.TransferDestination | TextureUsage.TransferSource);
        Texture? outputTex = separateTarget
            ? CreateHdrTexture(
                rendering,
                width,
                height,
                TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.TransferSource)
            : null;

        RenderGraph graph = new(rendering, new Extent2D(width, height), "BloomGpuTest");
        try
        {
            UploadRgba16Float(rendering, inputTex, hdrInput);

            RGNode_Bloom bloom = new(
                rendering,
                b => b.ImportTexture(inputTex, "InputHDR", separateTarget ? RGUsage.None : RGUsage.TransferRead),
                separateTarget
                    ? b => b.ImportTexture(outputTex!, "OutputHDR", RGUsage.TransferRead)
                    : null)
            {
                Threshold = threshold,
                Intensity = intensity,
                SizeScale = sizeScale,
                CompositeMode = mode,
                AlwaysProducesOutput = true,
            };
            for (int i = 0; i < StageCount; ++i)
            {
                bloom.Sizes[i] = sizes[i];
                bloom.Tints[i] = tints[i];
            }

            graph.Use(bloom);
            context.BeginFrame();
            graph.Execute(context);
            context.Submit();
            rendering.WaitIdle();

            if (!readback)
            {
                return [];
            }

            Texture readTarget = outputTex ?? inputTex;
            return ReadbackRgba16Float(rendering, readTarget, (int)width, (int)height);
        }
        finally
        {
            graph.Dispose();
            if (outputTex != null && !outputTex.IsDisposed)
            {
                outputTex.Dispose();
            }
            if (!inputTex.IsDisposed)
            {
                inputTex.Dispose();
            }
        }
    }

    private static Texture CreateHdrTexture(RenderingSystem rendering, uint width, uint height, TextureUsage usage)
    {
        return rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba16Float,
            Usage = usage,
        });
    }

    private static void UploadRgba16Float(RenderingSystem rendering, Texture texture, Vector4[] pixels)
    {
        Half[] packed = new Half[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; ++i)
        {
            int o = i * 4;
            packed[o + 0] = (Half)pixels[i].X;
            packed[o + 1] = (Half)pixels[i].Y;
            packed[o + 2] = (Half)pixels[i].Z;
            packed[o + 3] = (Half)pixels[i].W;
        }
        rendering.UploadTexture(texture, MemoryMarshal.AsBytes(packed.AsSpan()));
    }

    private static Vector4[] ReadbackRgba16Float(RenderingSystem rendering, Texture texture, int width, int height)
    {
        byte[] raw = new byte[width * height * 8];
        rendering.ReadTexture(texture, raw);
        ReadOnlySpan<Half> halves = MemoryMarshal.Cast<byte, Half>(raw);
        Vector4[] result = new Vector4[width * height];
        for (int i = 0; i < result.Length; ++i)
        {
            int o = i * 4;
            result[i] = new Vector4((float)halves[o + 0], (float)halves[o + 1], (float)halves[o + 2], (float)halves[o + 3]);
        }
        return result;
    }

    private static Vector4[] BuildTestHdrImage(int width, int height)
    {
        Vector4[] data = new Vector4[width * height];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                data[y * width + x] = new Vector4(0.2f, 0.3f, 0.4f, 0.75f);
            }
        }

        // Bright interior impulse + diagonal stroke.
        data[22 * width + 40] = new Vector4(20.0f, 8.0f, 2.0f, 0.5f);
        data[23 * width + 41] = new Vector4(12.0f, 6.0f, 1.5f, 0.5f);

        // Edge strips testing clamp-to-edge on left & bottom boundaries.
        data[0 * width + 0] = new Vector4(15.0f, 5.0f, 1.0f, 0.25f);
        data[(height - 1) * width + (width - 1)] = new Vector4(2.0f, 14.0f, 6.0f, 0.9f);

        // Negative values & NaN / +Inf pixels sanitized cleanly by BloomSetup.
        data[10 * width + 10] = new Vector4(-5.0f, 3.0f, -2.0f, 1.0f);
        data[11 * width + 10] = new Vector4(float.NaN, 1.0f, 1.0f, 1.0f);
        data[12 * width + 10] = new Vector4(1.0f, float.PositiveInfinity, 1.0f, 1.0f);
        return data;
    }

    private static Vector4[] BuildMultiTileHdrImage(int width, int height)
    {
        Vector4[] data = new Vector4[width * height];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                data[y * width + x] = new Vector4(0.1f, 0.1f, 0.1f, 1.0f);
            }
        }

        // Place bright spots near tile boundaries (x=63,64 and y=31,32) to verify LDS halo stitching.
        data[31 * width + 63] = new Vector4(16.0f, 4.0f, 1.0f, 1.0f);
        data[32 * width + 64] = new Vector4(1.0f, 16.0f, 4.0f, 1.0f);
        data[90 * width + 190] = new Vector4(8.0f, 10.0f, 12.0f, 1.0f);
        return data;
    }

    private static void AssertImagesMatch(
        Vector4[] expected,
        Vector4[] actual,
        Vector4[] input,
        int width,
        int height,
        float maxAbsTol,
        float relTol,
        string label)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        float maxDiff = 0.0f;
        int worstIndex = -1;

        for (int i = 0; i < expected.Length; ++i)
        {
            // Skip checking RGB exact equality on raw NaN/Inf source pixels because composite keeps scene input + bloom
            // (Setup only sanitizes the bloom extraction texture, matching original Bloom behaviour).
            if (!float.IsFinite(input[i].X) || !float.IsFinite(input[i].Y) || !float.IsFinite(input[i].Z))
            {
                continue;
            }

            Assert.That(float.IsFinite(actual[i].X) && float.IsFinite(actual[i].Y) && float.IsFinite(actual[i].Z), Is.True,
                $"{label}: NaN/Inf leaked into pixel {i % width},{i / width}!");

            float dx = MathF.Abs(expected[i].X - actual[i].X);
            float dy = MathF.Abs(expected[i].Y - actual[i].Y);
            float dz = MathF.Abs(expected[i].Z - actual[i].Z);
            float da = MathF.Abs(expected[i].W - actual[i].W);
            float d = MathF.Max(MathF.Max(dx, dy), dz);
            if (d > maxDiff)
            {
                maxDiff = d;
                worstIndex = i;
            }

            float scale = MathF.Max(MathF.Max(MathF.Abs(expected[i].X), MathF.Abs(expected[i].Y)), MathF.Abs(expected[i].Z));
            float allowed = MathF.Max(maxAbsTol, scale * relTol);
            Assert.That(d, Is.LessThanOrEqualTo(allowed),
                $"{label}: RGB mismatch at ({i % width},{i / width}): expected={expected[i]}, actual={actual[i]}, diff={d}, allowed={allowed}");
            Assert.That(da, Is.LessThanOrEqualTo(0.01f),
                $"{label}: Alpha was not preserved at ({i % width},{i / width}): expected={expected[i].W}, actual={actual[i].W}");
        }

        TestContext.WriteLine($"{label} ({width}x{height}): max RGB diff = {maxDiff:F6} at pixel {(worstIndex >= 0 ? $"({worstIndex % width},{worstIndex / width})" : "none")}");
    }

    // Exact CPU golden reference replicating the original BloomSetup + BloomDownsample +
    // bilinear-paired BloomGaussian + BloomBlit algorithm, including fp16 intermediate storage.
    private static Vector4[] ComputeOriginalBloomGoldenReference(
        Vector4[] input,
        int width,
        int height,
        float threshold,
        float intensity,
        float sizeScale,
        float[] sizes,
        Vector3[] tints)
    {
        int w0 = Math.Max(width >> 1, 1);
        int h0 = Math.Max(height >> 1, 1);
        Vector4[][] pyramid = new Vector4[StageCount][];
        int[] widths = new int[StageCount];
        int[] heights = new int[StageCount];
        widths[0] = w0;
        heights[0] = h0;
        for (int level = 1; level < StageCount; ++level)
        {
            widths[level] = Math.Max(widths[level - 1] >> 1, 1);
            heights[level] = Math.Max(heights[level - 1] >> 1, 1);
        }

        // Setup
        pyramid[0] = new Vector4[w0 * h0];
        for (int y = 0; y < h0; ++y)
        {
            float v = (y + 0.5f) / h0;
            for (int x = 0; x < w0; ++x)
            {
                float u = (x + 0.5f) / w0;
                Vector4 c = SampleLinearClamp(input, width, height, u, v);
                if (!float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z))
                {
                    pyramid[0][y * w0 + x] = new Vector4(0, 0, 0, 1);
                    continue;
                }
                Vector3 lin = new(MathF.Max(c.X, 0f), MathF.Max(c.Y, 0f), MathF.Max(c.Z, 0f));
                float amount = 1.0f;
                if (threshold >= 0.0f)
                {
                    float luma = lin.X * 0.3f + lin.Y * 0.59f + lin.Z * 0.11f;
                    amount = Math.Clamp((luma - threshold) * 0.5f, 0.0f, 1.0f);
                }
                pyramid[0][y * w0 + x] = QuantizeHalf(new Vector4(lin * amount, 1.0f));
            }
        }

        // Downsample 1..5
        for (int level = 1; level < StageCount; ++level)
        {
            int sw = widths[level - 1];
            int sh = heights[level - 1];
            int dw = widths[level];
            int dh = heights[level];
            Vector4[] src = pyramid[level - 1];
            Vector4[] dst = pyramid[level] = new Vector4[dw * dh];
            float invSx = 1.0f / sw;
            float invSy = 1.0f / sh;
            for (int y = 0; y < dh; ++y)
            {
                float v = (y + 0.5f) / dh;
                for (int x = 0; x < dw; ++x)
                {
                    float u = (x + 0.5f) / dw;
                    Vector4 c0 = SampleLinearClamp(src, sw, sh, u - invSx, v - invSy);
                    Vector4 c1 = SampleLinearClamp(src, sw, sh, u + invSx, v - invSy);
                    Vector4 c2 = SampleLinearClamp(src, sw, sh, u - invSx, v + invSy);
                    Vector4 c3 = SampleLinearClamp(src, sw, sh, u + invSx, v + invSy);
                    Vector3 avg = new Vector3(c0.X + c1.X + c2.X + c3.X, c0.Y + c1.Y + c2.Y + c3.Y, c0.Z + c1.Z + c2.Z + c3.Z) * 0.25f;
                    dst[y * dw + x] = QuantizeHalf(new Vector4(MathF.Max(avg.X, 0f), MathF.Max(avg.Y, 0f), MathF.Max(avg.Z, 0f), 1.0f));
                }
            }
        }

        // Coarse-to-fine Gaussian stages using the exact original bilinear-paired loop.
        float tintScale = intensity / StageCount;
        Vector4[]? accumulated = null;
        int accumW = 0;
        int accumH = 0;

        for (int stage = 0; stage < StageCount; ++stage)
        {
            int sizeIndex = StageCount - 1 - stage;
            float size = sizes[sizeIndex] * sizeScale;
            if (size <= float.Epsilon)
            {
                continue;
            }

            int sw = widths[sizeIndex];
            int sh = heights[sizeIndex];
            float radius = sw * size * 0.005f;
            float sigmaRadius = Math.Clamp(radius, 0.0001f, MaxRadius);
            int integerRadius = Math.Min(Math.Max((int)MathF.Ceiling(sigmaRadius), 1), (int)MaxRadius);

            Vector4[] horiz = RunOriginalGaussianPass(
                pyramid[sizeIndex], sw, sh, null, 0, 0,
                1f, 0f, sigmaRadius, integerRadius, Vector3.One, false);

            Vector3 tint = tints[sizeIndex] * tintScale;
            Vector4[] vert = RunOriginalGaussianPass(
                horiz, sw, sh, accumulated, accumW, accumH,
                0f, 1f, sigmaRadius, integerRadius, tint, accumulated != null);

            accumulated = vert;
            accumW = sw;
            accumH = sh;
        }

        // Final composite onto full-resolution scene input.
        Vector4[] output = new Vector4[width * height];
        for (int y = 0; y < height; ++y)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; ++x)
            {
                Vector4 scene = QuantizeHalf(input[y * width + x]);
                if (accumulated != null)
                {
                    float u = (x + 0.5f) / width;
                    Vector4 bloomSample = SampleLinearClamp(accumulated, accumW, accumH, u, v);
                    scene = new Vector4(scene.X + bloomSample.X, scene.Y + bloomSample.Y, scene.Z + bloomSample.Z, scene.W);
                }
                output[y * width + x] = QuantizeHalf(scene);
            }
        }
        return output;
    }

    private static Vector4[] RunOriginalGaussianPass(
        Vector4[] source,
        int width,
        int height,
        Vector4[]? additive,
        int addW,
        int addH,
        float dirX,
        float dirY,
        float sigmaRadius,
        int integerRadius,
        Vector3 tint,
        bool additiveBlend)
    {
        Vector4[] dst = new Vector4[width * height];
        float invW = 1.0f / width;
        float invH = 1.0f / height;

        for (int y = 0; y < height; ++y)
        {
            float v = (y + 0.5f) * invH;
            for (int x = 0; x < width; ++x)
            {
                float u = (x + 0.5f) * invW;
                Vector3 sum = Vector3.Zero;
                float weightSum = 0.0f;

                for (int i = -integerRadius; i <= integerRadius; i += 2)
                {
                    float d0 = i / sigmaRadius;
                    float w0 = MathF.Exp(-16.7f * d0 * d0);
                    float w1 = 0.0f;
                    if (i != integerRadius)
                    {
                        float d1 = (i + 1.0f) / sigmaRadius;
                        w1 = MathF.Exp(-16.7f * d1 * d1);
                    }

                    float totalWeight = w0 + w1;
                    float offset = i + w1 / totalWeight;
                    weightSum += totalWeight;

                    float su = u + dirX * offset * invW;
                    float sv = v + dirY * offset * invH;
                    Vector4 s = SampleLinearClamp(source, width, height, su, sv);
                    sum += new Vector3(s.X, s.Y, s.Z) * totalWeight;
                }

                Vector3 color = (sum / weightSum) * tint;
                if (additiveBlend && additive != null)
                {
                    Vector4 a = SampleLinearClamp(additive, addW, addH, u, v);
                    color += new Vector3(a.X, a.Y, a.Z);
                }

                dst[y * width + x] = QuantizeHalf(new Vector4(color, 1.0f));
            }
        }
        return dst;
    }

    private static Vector4 SampleLinearClamp(Vector4[] image, int width, int height, float u, float v)
    {
        float px = Math.Clamp(u * width - 0.5f, 0.0f, width - 1.0f);
        float py = Math.Clamp(v * height - 0.5f, 0.0f, height - 1.0f);
        int x0 = (int)MathF.Floor(px);
        int y0 = (int)MathF.Floor(py);
        int x1 = Math.Min(x0 + 1, width - 1);
        int y1 = Math.Min(y0 + 1, height - 1);
        float fx = px - x0;
        float fy = py - y0;

        Vector4 c00 = image[y0 * width + x0];
        Vector4 c10 = image[y0 * width + x1];
        Vector4 c01 = image[y1 * width + x0];
        Vector4 c11 = image[y1 * width + x1];

        Vector4 top = Vector4.Lerp(c00, c10, fx);
        Vector4 bottom = Vector4.Lerp(c01, c11, fx);
        return Vector4.Lerp(top, bottom, fy);
    }

    private static Vector4 QuantizeHalf(Vector4 v) =>
        new((float)(Half)v.X, (float)(Half)v.Y, (float)(Half)v.Z, (float)(Half)v.W);

    private static string LoadEmbeddedShader(string logicalName)
    {
        Assembly assembly = typeof(RGNode_Bloom).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new FileNotFoundException($"Embedded shader '{logicalName}' not found.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class LambdaNode(Action<RenderGraphBuilder> setup) : IRenderGraphNode
    {
        public void Setup(RenderGraphBuilder builder) => setup(builder);
        public void Execute(in RenderGraphContext context, int passId) { }
    }
}
