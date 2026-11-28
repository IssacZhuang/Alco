using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// CPU RenderGraph planning/routing/zero-GC tests and multi-backend GPU mathematical golden parity tests for <see cref="RGNode_Tonemap"/>.
/// </summary>
[TestFixture]
public unsafe class TestTonemapNode
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedTonemapRoot
    {
        public uint Source;
        public uint Dest;
        public uint DestSizeX;
        public uint DestSizeY;
        public Vector2 InvDestSize;
        public float Exposure;
        public float Gamma;
        public float AcesA;
        public float AcesB;
        public float AcesC;
        public float AcesD;
        public float AcesE;
        public float NeutralStartCompression;
        public float NeutralDesaturation;
        public uint Pad0;
        public GPUBufferView ExposureState;
    }

    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void TonemapShaders_CompileAndMatchRootLayouts_AcrossAllTargetsAndVariants(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
        {
            Assert.Ignore($"Shader target {target} is unsupported on this host.");
            return;
        }

        using ShaderCompiler compiler = new(target);
        compiler.LoadModuleFromSource(
            "AlcoRendering_AutoExposure",
            "AlcoRendering_AutoExposure.slang",
            LoadEmbeddedShader("AlcoRendering_AutoExposure.slang"));
        compiler.LoadModuleFromSource(
            "AlcoRendering_Tonemap",
            "AlcoRendering_Tonemap.slang",
            LoadEmbeddedShader("AlcoRendering_Tonemap.slang"));

        ShaderModule computeModule = compiler.LoadModuleFromSource(
            "TonemapCompute",
            "TonemapCompute.slang",
            LoadEmbeddedShader("TonemapCompute.slang"));
        ShaderModule graphicsModule = compiler.LoadModuleFromSource(
            "TonemapGraphics",
            "TonemapGraphics.slang",
            LoadEmbeddedShader("TonemapGraphics.slang"));

        for (int op = 0; op <= (int)TonemapType.Neutral; ++op)
        {
            for (int dither = 0; dither <= 1; ++dither)
            {
                for (int autoExp = 0; autoExp <= 1; ++autoExp)
                {
                    VerifyModuleLayout(computeModule, ShaderPipelineKind.Compute, op, dither, autoExp, target);
                    VerifyModuleLayout(graphicsModule, ShaderPipelineKind.Graphics, op, dither, autoExp, target);
                }
            }
        }
    }

    [Test]
    public void NodeConstruction_SupportsMultipleInstancesOnSharedRenderingSystem()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderChain chainA = new();
        RenderChain chainB = new();

        RGNode_Tonemap mainTonemap = new(host.RenderingSystem, chainA, name: "MainTonemap");
        RGNode_Tonemap overworldTonemap = new(host.RenderingSystem, chainB, name: "OverworldTonemap");

        Assert.That(mainTonemap.Name, Is.EqualTo("MainTonemap"));
        Assert.That(overworldTonemap.Name, Is.EqualTo("OverworldTonemap"));
        Assert.That(mainTonemap.Operator, Is.EqualTo(TonemapType.Neutral));
        Assert.That(mainTonemap.DitherEnabled, Is.True);
        Assert.That(mainTonemap.ExecutionMode, Is.EqualTo(TonemapExecutionMode.Auto));
        Assert.That(mainTonemap.OutputFormat, Is.Null);
    }

    [Test]
    public void RenderGraph_ChainRouting_FormatConvergence_Bypass_And_ZeroSteadyStateAllocations()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderContext context = new(host.RenderingSystem);
        RenderGraph graph = new(host.RenderingSystem, new Extent2D(1919, 1079), "TonemapNoGpuGraph");
        RenderChain chain = new();

        Texture hdrScene = host.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1919, 1079, 1u),
            Format = Format.Rgba16Float,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
        });
        Texture srgbScene = host.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1919, 1079, 1u),
            Format = Format.Rgba8Srgb,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
        });
        Texture colorOnlyOutput = host.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1919, 1079, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
        });

        try
        {
            RGTexture importedHdr = graph.ImportTexture(hdrScene, "SceneHDR", finalUsage: RGUsage.PixelRead);
            RGTexture capturedChainEnd = default;
            Format capturedChainFormat = Format.Undefined;

            RGNode_Tonemap tonemap = new(host.RenderingSystem, chain);

            graph.Use(new LambdaNode(b =>
            {
                chain.Reset(importedHdr);
                RenderGraphPassBuilder writePass = b.AddPass("HdrWrite");
                writePass.Write(importedHdr, RGUsage.ColorAttachmentWrite);
            }));
            graph.Use(tonemap);
            graph.Use(new LambdaNode(b =>
            {
                capturedChainEnd = chain.Current;
                capturedChainFormat = b.GetFormat(capturedChainEnd);
                RenderGraphPassBuilder consumePass = b.AddPass("Consume");
                consumePass.Read(capturedChainEnd, RGUsage.PixelRead);
                consumePass.ProducesOutput();
            }));

            // 1. Default Neutral on HDR chain converges Rgba16Float -> Rgba8Unorm and routes to Compute.
            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedChainEnd, Is.Not.EqualTo(importedHdr));
            Assert.That(capturedChainFormat, Is.EqualTo(Format.Rgba8Unorm));
            Assert.That(graph.GetResolvedTextureUsage(capturedChainEnd).HasFlag(TextureUsage.Storage), Is.True);
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(3));

            // 2. Explicit OutputFormat override keeps Rgba16Float when requested.
            tonemap.OutputFormat = Format.Rgba16Float;
            tonemap.Operator = TonemapType.ACES;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedChainFormat, Is.EqualTo(Format.Rgba16Float));

            // 3. Linear operator with OutputFormat == null preserves input format (Rgba16Float).
            tonemap.OutputFormat = null;
            tonemap.Operator = TonemapType.Linear;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedChainFormat, Is.EqualTo(Format.Rgba16Float));

            // 4. Chain Bypass skips Tonemap pass completely while preserving chain.Current.
            tonemap.Operator = TonemapType.Neutral;
            tonemap.Bypass = true;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedChainEnd, Is.EqualTo(importedHdr));
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(2));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(2));

            // 5. Steady-state zero GC allocations on Compute chain path.
            tonemap.Bypass = false;
            for (int i = 0; i < 4; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }

            long beforeComputeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }
            long computeAlloc = GC.GetAllocatedBytesForCurrentThread() - beforeComputeBytes;
            Assert.That(computeAlloc, Is.Zero,
                $"Steady-state Compute Tonemap must allocate 0 managed bytes (allocated={computeAlloc}).");

            // 6. Explicit sRGB / ColorAttachment-only targets route to Graphics and allocate 0 bytes.
            RenderGraph graphicsGraph = new(host.RenderingSystem, new Extent2D(1919, 1079), "TonemapGraphicsRouting");
            try
            {
                RGTexture srgbHandle = graphicsGraph.ImportTexture(srgbScene, "SrgbIn");
                RGTexture colorOnlyHandle = graphicsGraph.ImportTexture(colorOnlyOutput, "ColorOnlyOut", RGUsage.TransferRead);
                RGNode_Tonemap gfxNode = new(
                    host.RenderingSystem,
                    _ => srgbHandle,
                    _ => colorOnlyHandle)
                {
                    Operator = TonemapType.ACES,
                    AlwaysProducesOutput = true,
                };
                graphicsGraph.Use(gfxNode);

                for (int i = 0; i < 4; ++i)
                {
                    ExecuteNoGpuFrame(graphicsGraph, context);
                }

                long beforeGfxBytes = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 30; ++i)
                {
                    ExecuteNoGpuFrame(graphicsGraph, context);
                }
                long gfxAlloc = GC.GetAllocatedBytesForCurrentThread() - beforeGfxBytes;
                Assert.That(gfxAlloc, Is.Zero,
                    $"Steady-state Graphics Tonemap must allocate 0 managed bytes (allocated={gfxAlloc}).");

                // Same-texture in-place throws explicitly.
                RenderGraph badInPlace = new(host.RenderingSystem, new Extent2D(64, 64), "BadInPlace");
                try
                {
                    RGTexture h = badInPlace.ImportTexture(hdrScene, "Same");
                    badInPlace.Use(new RGNode_Tonemap(host.RenderingSystem, _ => h, _ => h) { AlwaysProducesOutput = true });
                    Assert.Throws<InvalidOperationException>(() => badInPlace.CompileDryRun());
                }
                finally
                {
                    badInPlace.Dispose();
                }
            }
            finally
            {
                graphicsGraph.Dispose();
            }
        }
        finally
        {
            graph.Dispose();
            colorOnlyOutput.Dispose();
            srgbScene.Dispose();
            hdrScene.Dispose();
            context.Dispose();
        }
    }

    [TestCase(GraphicsBackend.NoGPU)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void Gpu_AcesNeutralLinearAndParity_MatchOriginalGoldenReference(GraphicsBackend backend)
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
            const uint width = 129;
            const uint height = 65;
            Vector4[] hdrInput = BuildTestHdrImage((int)width, (int)height);

            ACESTonemapData customAces = new()
            {
                A = 2.51f,
                B = 0.03f,
                C = 2.43f,
                D = 0.59f,
                E = 0.14f,
                Exposure = 1.4f,
                Gamma = 2.2f,
            };
            NeutralTonemapData customNeutral = new()
            {
                Exposure = 1.35f,
                Gamma = 2.2f,
                StartCompression = 0.76f,
                Desaturation = 0.15f,
            };

            if (backend == GraphicsBackend.NoGPU)
            {
                _ = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                    TonemapType.Neutral, NeutralTonemapData.Default, ACESTonemapData.Default,
                    dither: true, bypass: false, TonemapExecutionMode.Compute, readback: false);
                _ = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                    TonemapType.ACES, NeutralTonemapData.Default, customAces,
                    dither: true, bypass: false, TonemapExecutionMode.Graphics, readback: false);
                _ = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                    TonemapType.Linear, NeutralTonemapData.Default, ACESTonemapData.Default,
                    dither: false, bypass: true, TonemapExecutionMode.Auto, readback: false);
                return;
            }

            // 1. Neutral (default parameters + dither): Compute vs Graphics parity & Golden Reference match.
            byte[] neutralCompute = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                TonemapType.Neutral, NeutralTonemapData.Default, ACESTonemapData.Default,
                dither: true, bypass: false, TonemapExecutionMode.Compute);
            byte[] neutralGraphics = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                TonemapType.Neutral, NeutralTonemapData.Default, ACESTonemapData.Default,
                dither: true, bypass: false, TonemapExecutionMode.Graphics);

            AssertByteImagesClose(neutralCompute, neutralGraphics, (int)width, (int)height, maxChannelDiff: 1, "NeutralComputeVsGraphics");
            byte[] neutralGolden = ComputeOriginalTonemapGoldenRgba8(
                hdrInput, (int)width, (int)height, TonemapType.Neutral,
                NeutralTonemapData.Default, ACESTonemapData.Default, dither: true);
            AssertFiniteRegionMatchesGolden(hdrInput, neutralCompute, neutralGolden, (int)width, (int)height, maxChannelDiff: 2, "NeutralDefaultCompute");
            AssertFiniteRegionMatchesGolden(hdrInput, neutralGraphics, neutralGolden, (int)width, (int)height, maxChannelDiff: 2, "NeutralDefaultGraphics");
            AssertAllPixelsFinite(neutralCompute, (int)width, (int)height, "NeutralSanitized");

            // 2. Neutral (custom Exposure + Gamma=2.2 + StartCompression=0.76 + Desaturation=0.15).
            byte[] neutralCustomGfx = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                TonemapType.Neutral, customNeutral, ACESTonemapData.Default,
                dither: true, bypass: false, TonemapExecutionMode.Graphics);
            byte[] neutralCustomGolden = ComputeOriginalTonemapGoldenRgba8(
                hdrInput, (int)width, (int)height, TonemapType.Neutral,
                customNeutral, ACESTonemapData.Default, dither: true);
            AssertFiniteRegionMatchesGolden(hdrInput, neutralCustomGfx, neutralCustomGolden, (int)width, (int)height, maxChannelDiff: 2, "NeutralCustomGamma");

            // 3. ACES (custom Exposure + Gamma=2.2): Compute vs Graphics parity & Golden Reference match.
            byte[] acesCompute = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                TonemapType.ACES, NeutralTonemapData.Default, customAces,
                dither: true, bypass: false, TonemapExecutionMode.Compute);
            byte[] acesGraphics = ExecuteRgba8Tonemap(rendering, context, hdrInput, width, height,
                TonemapType.ACES, NeutralTonemapData.Default, customAces,
                dither: true, bypass: false, TonemapExecutionMode.Graphics);

            AssertByteImagesClose(acesCompute, acesGraphics, (int)width, (int)height, maxChannelDiff: 1, "AcesComputeVsGraphics");
            byte[] acesGolden = ComputeOriginalTonemapGoldenRgba8(
                hdrInput, (int)width, (int)height, TonemapType.ACES,
                NeutralTonemapData.Default, customAces, dither: true);
            AssertFiniteRegionMatchesGolden(hdrInput, acesCompute, acesGolden, (int)width, (int)height, maxChannelDiff: 2, "AcesCustomCompute");
            AssertFiniteRegionMatchesGolden(hdrInput, acesGraphics, acesGolden, (int)width, (int)height, maxChannelDiff: 2, "AcesCustomGraphics");

            // 4. Verify DitherEnabled = false removes +/-1/255 dither offset (exact match to no-dither fp16 golden).
            Vector4[] acesNoDitherHdr = ExecuteHdrOutputTonemap(rendering, context, hdrInput, width, height,
                TonemapType.ACES, NeutralTonemapData.Default, ACESTonemapData.Default, dither: false);
            Vector4[] acesNoDitherGolden = ComputeOriginalTonemapGoldenHdr(
                hdrInput, (int)width, (int)height, TonemapType.ACES,
                NeutralTonemapData.Default, ACESTonemapData.Default, dither: false);
            AssertHdrImagesMatch(hdrInput, acesNoDitherGolden, acesNoDitherHdr, (int)width, (int)height, maxAbsTol: 0.002f, "AcesNoDitherFp16");

            // 5. Linear operator preserves HDR (>1.0) values and varying alpha on Rgba16Float chain.
            Vector4[] linearOut = ExecuteHdrOutputTonemap(rendering, context, hdrInput, width, height,
                TonemapType.Linear, NeutralTonemapData.Default, ACESTonemapData.Default, dither: true);
            for (int i = 0; i < hdrInput.Length; ++i)
            {
                if (!float.IsFinite(hdrInput[i].X) || !float.IsFinite(hdrInput[i].Y) || !float.IsFinite(hdrInput[i].Z))
                {
                    continue;
                }
                Vector4 q = QuantizeHalf(hdrInput[i]);
                Assert.That(MathF.Abs(linearOut[i].X - q.X), Is.LessThanOrEqualTo(0.002f));
                Assert.That(MathF.Abs(linearOut[i].Y - q.Y), Is.LessThanOrEqualTo(0.002f));
                Assert.That(MathF.Abs(linearOut[i].Z - q.Z), Is.LessThanOrEqualTo(0.002f));
                Assert.That(MathF.Abs(linearOut[i].W - q.W), Is.LessThanOrEqualTo(0.002f), "Linear must preserve source alpha.");
            }

            // 6. Direct output to builder.Output (non-Storage) auto-routes to Graphics and matches golden.
            VerifyDirectBuilderOutput(rendering, context, hdrInput, width, height, neutralGolden);

            // 7. Explicit Bypass on non-chain target copies source RGBA unchanged without tonemapping.
            VerifyBypassPassthrough(rendering, context, hdrInput, width, height);
        }
        finally
        {
            device.WaitIdle();
            context.Dispose();
            rendering.Dispose();
            device.Destroy();
        }
    }

    private static void VerifyModuleLayout(
        ShaderModule module,
        ShaderPipelineKind kind,
        int op,
        int dither,
        int autoExp,
        ShaderTarget target)
    {
        CompiledShader compiled = module.Compile(kind, new ShaderCompileOptions
        {
            SpecializationArgs =
            [
                op.ToString(),
                dither.ToString(),
                autoExp.ToString(),
            ],
        });

        Assert.That(compiled.Reflection.Root, Is.Not.Null,
            $"{module.Name}<{op},{dither},{autoExp}> must reflect TonemapRoot on {target}.");
        Assert.That(compiled.Reflection.Root!.SizeBytes, Is.EqualTo((uint)sizeof(ExpectedTonemapRoot)),
            $"{module.Name}<{op},{dither},{autoExp}> root size mismatch on {target}.");
        Assert.That(compiled.Reflection.MatchesStructLayout<ExpectedTonemapRoot>("TonemapRoot"), Is.True,
            $"{module.Name}<{op},{dither},{autoExp}> root field layout mismatch on {target}.");
    }

    private static void ExecuteNoGpuFrame(RenderGraph graph, RenderContext context)
    {
        context.BeginFrame();
        graph.Execute(context);
        context.Submit();
    }

    private static byte[] ExecuteRgba8Tonemap(
        RenderingSystem rendering,
        RenderContext context,
        Vector4[] hdrInput,
        uint width,
        uint height,
        TonemapType op,
        NeutralTonemapData neutralData,
        ACESTonemapData acesData,
        bool dither,
        bool bypass,
        TonemapExecutionMode executionMode,
        bool readback = true)
    {
        Texture src = CreateHdrTexture(rendering, width, height,
            TextureUsage.Sampled | TextureUsage.TransferDestination);
        Texture dst = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Storage | TextureUsage.TransferSource,
        });

        RenderGraph graph = new(rendering, new Extent2D(width, height), "TonemapRgba8Gpu");
        try
        {
            UploadRgba16Float(rendering, src, hdrInput);
            RGNode_Tonemap tonemap = new(
                rendering,
                b => b.ImportTexture(src, "ToneSrc"),
                b => b.ImportTexture(dst, "ToneDst", RGUsage.TransferRead))
            {
                Operator = op,
                NeutralData = neutralData,
                ACESData = acesData,
                DitherEnabled = dither,
                Bypass = bypass,
                ExecutionMode = executionMode,
                AlwaysProducesOutput = true,
            };
            graph.Use(tonemap);

            context.BeginFrame();
            graph.Execute(context);
            context.Submit();
            rendering.WaitIdle();

            if (!readback)
            {
                return [];
            }

            byte[] result = new byte[width * height * 4];
            rendering.ReadTexture(dst, result);
            return result;
        }
        finally
        {
            graph.Dispose();
            if (!dst.IsDisposed) dst.Dispose();
            if (!src.IsDisposed) src.Dispose();
        }
    }

    private static Vector4[] ExecuteHdrOutputTonemap(
        RenderingSystem rendering,
        RenderContext context,
        Vector4[] hdrInput,
        uint width,
        uint height,
        TonemapType op,
        NeutralTonemapData neutralData,
        ACESTonemapData acesData,
        bool dither)
    {
        Texture src = CreateHdrTexture(rendering, width, height,
            TextureUsage.Sampled | TextureUsage.TransferDestination);
        Texture dst = CreateHdrTexture(rendering, width, height,
            TextureUsage.ColorAttachment | TextureUsage.Storage | TextureUsage.TransferSource);

        RenderGraph graph = new(rendering, new Extent2D(width, height), "TonemapHdrOutGpu");
        try
        {
            UploadRgba16Float(rendering, src, hdrInput);
            RGNode_Tonemap tonemap = new(
                rendering,
                b => b.ImportTexture(src, "ToneHdrSrc"),
                b => b.ImportTexture(dst, "ToneHdrDst", RGUsage.TransferRead))
            {
                Operator = op,
                NeutralData = neutralData,
                ACESData = acesData,
                DitherEnabled = dither,
                AlwaysProducesOutput = true,
            };
            graph.Use(tonemap);

            context.BeginFrame();
            graph.Execute(context);
            context.Submit();
            rendering.WaitIdle();

            return ReadbackRgba16Float(rendering, dst, (int)width, (int)height);
        }
        finally
        {
            graph.Dispose();
            if (!dst.IsDisposed) dst.Dispose();
            if (!src.IsDisposed) src.Dispose();
        }
    }

    private static void VerifyDirectBuilderOutput(
        RenderingSystem rendering,
        RenderContext context,
        Vector4[] hdrInput,
        uint width,
        uint height,
        byte[] expectedGolden)
    {
        Texture src = CreateHdrTexture(rendering, width, height,
            TextureUsage.Sampled | TextureUsage.TransferDestination);
        Texture output = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource, // No Storage -> forces Graphics.
        });

        RenderGraph graph = new(rendering, new Extent2D(width, height), "TonemapDirectOutput");
        try
        {
            UploadRgba16Float(rendering, src, hdrInput);
            RGNode_Tonemap tonemap = new(rendering, b => b.ImportTexture(src, "OutSrc"))
            {
                Operator = TonemapType.Neutral,
                DitherEnabled = true,
                ExecutionMode = TonemapExecutionMode.Auto,
            };
            graph.Use(tonemap);

            context.BeginFrame();
            graph.Execute(context, RenderGraphTarget.FromTexture(output), outputFinalUsage: RGUsage.TransferRead);
            context.Submit();
            rendering.WaitIdle();

            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(1));
            byte[] actual = new byte[width * height * 4];
            rendering.ReadTexture(output, actual);
            AssertFiniteRegionMatchesGolden(hdrInput, actual, expectedGolden, (int)width, (int)height, maxChannelDiff: 2, "DirectBuilderOutput");
        }
        finally
        {
            graph.Dispose();
            if (!output.IsDisposed) output.Dispose();
            if (!src.IsDisposed) src.Dispose();
        }
    }

    private static void VerifyBypassPassthrough(
        RenderingSystem rendering,
        RenderContext context,
        Vector4[] hdrInput,
        uint width,
        uint height)
    {
        Texture src = CreateHdrTexture(rendering, width, height,
            TextureUsage.Sampled | TextureUsage.TransferDestination);
        Texture dst = CreateHdrTexture(rendering, width, height,
            TextureUsage.ColorAttachment | TextureUsage.Storage | TextureUsage.TransferSource);

        RenderGraph graph = new(rendering, new Extent2D(width, height), "TonemapBypass");
        try
        {
            UploadRgba16Float(rendering, src, hdrInput);
            RGNode_Tonemap tonemap = new(
                rendering,
                b => b.ImportTexture(src, "BypassSrc"),
                b => b.ImportTexture(dst, "BypassDst", RGUsage.TransferRead))
            {
                Operator = TonemapType.ACES,
                Bypass = true,
                AlwaysProducesOutput = true,
            };
            graph.Use(tonemap);

            context.BeginFrame();
            graph.Execute(context);
            context.Submit();
            rendering.WaitIdle();

            Vector4[] actual = ReadbackRgba16Float(rendering, dst, (int)width, (int)height);
            for (int i = 0; i < hdrInput.Length; ++i)
            {
                if (!float.IsFinite(hdrInput[i].X) || !float.IsFinite(hdrInput[i].Y) || !float.IsFinite(hdrInput[i].Z))
                {
                    continue;
                }
                Vector4 expected = QuantizeHalf(hdrInput[i]);
                Assert.That(MathF.Abs(actual[i].X - expected.X), Is.LessThanOrEqualTo(0.002f));
                Assert.That(MathF.Abs(actual[i].Y - expected.Y), Is.LessThanOrEqualTo(0.002f));
                Assert.That(MathF.Abs(actual[i].Z - expected.Z), Is.LessThanOrEqualTo(0.002f));
                Assert.That(MathF.Abs(actual[i].W - expected.W), Is.LessThanOrEqualTo(0.002f));
            }
        }
        finally
        {
            graph.Dispose();
            if (!dst.IsDisposed) dst.Dispose();
            if (!src.IsDisposed) src.Dispose();
        }
    }

    private static Vector4[] BuildTestHdrImage(int width, int height)
    {
        Vector4[] data = new Vector4[width * height];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                float u = x / (float)Math.Max(width - 1, 1);
                float v = y / (float)Math.Max(height - 1, 1);
                // Smooth HDR ramp across toe, linear, compression, and bright saturation zones, plus varying alpha.
                float scale = MathF.Pow(2.0f, u * 8.0f - 3.0f); // ~0.125 .. 32.0
                float r = scale * (0.25f + 0.75f * v);
                float g = scale * (0.6f - 0.4f * v);
                float b = scale * (0.15f + 0.85f * (1.0f - v));
                float a = 0.25f + 0.65f * ((x + y) % 10) / 9.0f;
                data[y * width + x] = new Vector4(r, g, b, a);
            }
        }

        // Pure black, toe, and extreme saturated HDR highlights.
        data[0] = new Vector4(0.0f, 0.0f, 0.0f, 0.3f);
        data[1] = new Vector4(0.03f, 0.05f, 0.02f, 0.5f);
        data[2] = new Vector4(24.0f, 0.4f, 0.1f, 0.7f);
        data[3] = new Vector4(0.2f, 18.0f, 1.5f, 0.9f);

        // Edge pixels with negative channel, NaN, and +Inf to verify sanitization.
        data[10 * width + 10] = new Vector4(-0.5f, 0.4f, 0.2f, 1.0f);
        data[11 * width + 10] = new Vector4(float.NaN, 1.0f, 0.5f, 1.0f);
        data[12 * width + 10] = new Vector4(1.0f, float.PositiveInfinity, 0.5f, 1.0f);
        return data;
    }

    private static byte[] ComputeOriginalTonemapGoldenRgba8(
        Vector4[] input,
        int width,
        int height,
        TonemapType op,
        NeutralTonemapData neutral,
        ACESTonemapData aces,
        bool dither)
    {
        Vector4[] hdr = ComputeOriginalTonemapGoldenHdr(input, width, height, op, neutral, aces, dither);
        byte[] bytes = new byte[width * height * 4];
        for (int i = 0; i < hdr.Length; ++i)
        {
            int o = i * 4;
            bytes[o + 0] = (byte)MathF.Round(Math.Clamp(hdr[i].X, 0.0f, 1.0f) * 255.0f);
            bytes[o + 1] = (byte)MathF.Round(Math.Clamp(hdr[i].Y, 0.0f, 1.0f) * 255.0f);
            bytes[o + 2] = (byte)MathF.Round(Math.Clamp(hdr[i].Z, 0.0f, 1.0f) * 255.0f);
            bytes[o + 3] = (byte)MathF.Round(Math.Clamp(hdr[i].W, 0.0f, 1.0f) * 255.0f);
        }
        return bytes;
    }

    private static Vector4[] ComputeOriginalTonemapGoldenHdr(
        Vector4[] input,
        int width,
        int height,
        TonemapType op,
        NeutralTonemapData neutral,
        ACESTonemapData aces,
        bool dither)
    {
        Vector4[] output = new Vector4[width * height];
        for (int y = 0; y < height; ++y)
        {
            float py = y + 0.5f;
            for (int x = 0; x < width; ++x)
            {
                float px = x + 0.5f;
                int idx = y * width + x;
                Vector4 q = QuantizeHalf(input[idx]);
                if (op == TonemapType.Linear)
                {
                    output[idx] = q;
                    continue;
                }

                Vector3 hdr = new(q.X, q.Y, q.Z);
                if (!float.IsFinite(hdr.X) || !float.IsFinite(hdr.Y) || !float.IsFinite(hdr.Z))
                {
                    output[idx] = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
                    continue;
                }

                Vector3 ldr;
                float gamma;
                if (op == TonemapType.ACES)
                {
                    Vector3 v = hdr * aces.Exposure;
                    ldr = new Vector3(
                        Math.Clamp((v.X * (aces.A * v.X + aces.B)) / (v.X * (aces.C * v.X + aces.D) + aces.E), 0.0f, 1.0f),
                        Math.Clamp((v.Y * (aces.A * v.Y + aces.B)) / (v.Y * (aces.C * v.Y + aces.D) + aces.E), 0.0f, 1.0f),
                        Math.Clamp((v.Z * (aces.A * v.Z + aces.B)) / (v.Z * (aces.C * v.Z + aces.D) + aces.E), 0.0f, 1.0f));
                    gamma = aces.Gamma;
                }
                else
                {
                    Vector3 c = hdr * neutral.Exposure;
                    float minC = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
                    float offset = minC < 0.08f ? (minC - 6.25f * minC * minC) : 0.04f;
                    c -= new Vector3(offset);
                    float peak = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
                    if (peak >= neutral.StartCompression)
                    {
                        float d = 1.0f - neutral.StartCompression;
                        float newPeak = 1.0f - (d * d) / (peak + d - neutral.StartCompression);
                        c *= newPeak / peak;
                        float g = 1.0f - 1.0f / (neutral.Desaturation * (peak - newPeak) + 1.0f);
                        c = Vector3.Lerp(c, new Vector3(newPeak), g);
                    }
                    ldr = c;
                    gamma = neutral.Gamma;
                }

                ldr = new Vector3(MathF.Max(ldr.X, 0.0f), MathF.Max(ldr.Y, 0.0f), MathF.Max(ldr.Z, 0.0f));
                if (MathF.Abs(gamma - 1.0f) > 1e-5f)
                {
                    float invGamma = 1.0f / gamma;
                    ldr = new Vector3(
                        ldr.X > 0.0f ? MathF.Pow(ldr.X, invGamma) : 0.0f,
                        ldr.Y > 0.0f ? MathF.Pow(ldr.Y, invGamma) : 0.0f,
                        ldr.Z > 0.0f ? MathF.Pow(ldr.Z, invGamma) : 0.0f);
                }

                if (dither)
                {
                    float ditherVal = OutputDither8Bit(px, py);
                    ldr += new Vector3(ditherVal);
                }

                output[idx] = new Vector4(ldr, 1.0f);
            }
        }
        return output;
    }

    private static float OutputDither8Bit(float x, float y)
    {
        float dot1 = x * 0.06711056f + y * 0.00583715f;
        float dot2 = (x + 0.5f) * 0.06711056f + (y + 0.5f) * 0.00583715f;
        float r1 = Frac(52.9829189f * Frac(dot1));
        float r2 = Frac(52.9829189f * Frac(dot2));
        return (r1 + r2 - 1.0f) * (1.0f / 255.0f);
    }

    private static float Frac(float v) => v - MathF.Floor(v);

    private static void AssertByteImagesClose(byte[] a, byte[] b, int width, int height, int maxChannelDiff, string label)
    {
        int maxDiff = 0;
        for (int i = 0; i < a.Length; ++i)
        {
            int diff = Math.Abs(a[i] - b[i]);
            if (diff > maxDiff) maxDiff = diff;
            Assert.That(diff, Is.LessThanOrEqualTo(maxChannelDiff),
                $"{label}: mismatch at ({(i / 4) % width},{(i / 4) / width}) ch {i % 4}: a={a[i]}, b={b[i]}");
        }
        TestContext.WriteLine($"{label} ({width}x{height}): max channel diff = {maxDiff}");
    }

    private static void AssertFiniteRegionMatchesGolden(
        Vector4[] input,
        byte[] actual,
        byte[] golden,
        int width,
        int height,
        int maxChannelDiff,
        string label)
    {
        int maxDiff = 0;
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                int i = y * width + x;
                if (!float.IsFinite(input[i].X) || !float.IsFinite(input[i].Y) || !float.IsFinite(input[i].Z))
                {
                    continue;
                }
                int o = i * 4;
                for (int c = 0; c < 4; ++c)
                {
                    int diff = Math.Abs(actual[o + c] - golden[o + c]);
                    if (diff > maxDiff) maxDiff = diff;
                    Assert.That(diff, Is.LessThanOrEqualTo(maxChannelDiff),
                        $"{label}: mismatch at ({x},{y}) ch {c}: actual={actual[o + c]}, golden={golden[o + c]}");
                }
            }
        }
        TestContext.WriteLine($"{label} ({width}x{height}): max channel diff vs golden = {maxDiff}");
    }

    private static void AssertAllPixelsFinite(byte[] actual, int width, int height, string label)
    {
        // Verify alpha=255 for all tonemapped pixels (including sanitized NaN/Inf spots).
        for (int i = 0; i < width * height; ++i)
        {
            Assert.That(actual[i * 4 + 3], Is.EqualTo(255), $"{label}: expected opaque alpha at pixel {i % width},{i / width}");
        }
    }

    private static void AssertHdrImagesMatch(
        Vector4[] input,
        Vector4[] expected,
        Vector4[] actual,
        int width,
        int height,
        float maxAbsTol,
        string label)
    {
        float maxDiff = 0.0f;
        for (int i = 0; i < expected.Length; ++i)
        {
            if (!float.IsFinite(input[i].X) || !float.IsFinite(input[i].Y) || !float.IsFinite(input[i].Z))
            {
                continue;
            }
            Assert.That(float.IsFinite(actual[i].X) && float.IsFinite(actual[i].Y) && float.IsFinite(actual[i].Z), Is.True);
            float d = MathF.Max(
                MathF.Max(MathF.Abs(expected[i].X - actual[i].X), MathF.Abs(expected[i].Y - actual[i].Y)),
                MathF.Max(MathF.Abs(expected[i].Z - actual[i].Z), MathF.Abs(expected[i].W - actual[i].W)));
            if (d > maxDiff) maxDiff = d;
            Assert.That(d, Is.LessThanOrEqualTo(maxAbsTol),
                $"{label}: fp16 mismatch at ({i % width},{i / width}): expected={expected[i]}, actual={actual[i]}");
        }
        TestContext.WriteLine($"{label} ({width}x{height}): max fp16 diff = {maxDiff:F6}");
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

    private static Vector4 QuantizeHalf(Vector4 v) =>
        new((float)(Half)v.X, (float)(Half)v.Y, (float)(Half)v.Z, (float)(Half)v.W);

    private static string LoadEmbeddedShader(string logicalName)
    {
        Assembly assembly = typeof(RGNode_Tonemap).Assembly;
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
