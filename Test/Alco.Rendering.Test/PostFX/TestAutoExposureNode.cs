using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>
/// CPU RenderGraph culling/telemetry/zero-GC tests and multi-backend GPU mathematical verification for <see cref="RGNode_AutoExposure"/>.
/// </summary>
[TestFixture]
public unsafe class TestAutoExposureNode
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedClearRoot
    {
        public GPUBufferView Histogram;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedHistogramRoot
    {
        public uint Source;
        public uint SampleSizeX;
        public uint SampleSizeY;
        public uint MeteringMode;
        public Vector2 InvSampleSize;
        public float MinLogLuminance;
        public float InvLogLuminanceRange;
        public GPUBufferView Histogram;
        public float CenterWeightRadius;
        public uint Pad0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedAdaptRoot
    {
        public GPUBufferView Histogram;
        public GPUBufferView State;
        public float MinLogLuminance;
        public float LogLuminanceRange;
        public float LowPercent;
        public float HighPercent;
        public float MinEV;
        public float MaxEV;
        public float ExposureCompensation;
        public float KeyValue;
        public float SpeedUp;
        public float SpeedDown;
        public float DeltaTime;
        public uint Flags;
    }

    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void AutoExposureShaders_CompileAndMatchRootLayouts_AcrossAllTargets(ShaderTarget target)
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

        VerifyTargetLayout<ExpectedClearRoot>(compiler, "AutoExposureClear", "AutoExposureClearRoot");
        VerifyTargetLayout<ExpectedHistogramRoot>(compiler, "AutoExposureHistogram", "AutoExposureHistogramRoot");
        VerifyTargetLayout<ExpectedAdaptRoot>(compiler, "AutoExposureAdapt", "AutoExposureAdaptRoot");
    }

    [Test]
    public void RenderGraph_DeadPassCulling_Telemetry_And_ZeroSteadyStateAllocations()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderContext context = new(host.RenderingSystem);
        RenderGraph graph = new(host.RenderingSystem, new Extent2D(1280, 720), "AutoExposureNoGpuGraph");
        RenderChain chain = new();

        Texture hdrScene = host.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1280, 720, 1u),
            Format = Format.Rgba16Float,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
        });

        using RGNode_AutoExposure autoExposure = new(host.RenderingSystem, chain);
        RGNode_Tonemap tonemap = new(host.RenderingSystem, chain)
        {
            Operator = TonemapType.Neutral,
            ExposureProvider = _ => autoExposure.OutputBuffer,
        };

        try
        {
            RGTexture importedHdr = graph.ImportTexture(hdrScene, "SceneHDR", finalUsage: RGUsage.PixelRead);

            graph.Use(new LambdaNode(b =>
            {
                chain.Reset(importedHdr);
                RenderGraphPassBuilder writePass = b.AddPass("HdrWrite");
                writePass.Write(importedHdr, RGUsage.ColorAttachmentWrite);
            }));
            graph.Use(autoExposure);
            graph.Use(tonemap);
            graph.Use(new LambdaNode(b =>
            {
                RenderGraphPassBuilder consumePass = b.AddPass("Consume");
                consumePass.Read(chain.Current, RGUsage.PixelRead);
                consumePass.ProducesOutput();
            }));

            // 1. Active consumer keeps all 3 AutoExposure passes alive.
            ExecuteNoGpuFrame(graph, context);
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(6));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(6));
            Assert.That(graph.Statistics.CulledPassCount, Is.Zero);

            // 2. Tonemap bypass removes consumer -> RenderGraph culls all 3 AutoExposure passes.
            tonemap.Bypass = true;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(5));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(2));
            Assert.That(graph.Statistics.CulledPassCount, Is.EqualTo(3));

            // 3. Linear tonemap skips exposure read -> AutoExposure passes are culled.
            tonemap.Bypass = false;
            tonemap.Operator = TonemapType.Linear;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(6));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(3));
            Assert.That(graph.Statistics.CulledPassCount, Is.EqualTo(3));

            // 4. Enabling telemetry keeps Clear, Histogram, Adapt, and Readback alive.
            autoExposure.TelemetryEnabled = true;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(7));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(7));
            Assert.That(graph.Statistics.CulledPassCount, Is.Zero);

            // 5. Steady-state zero managed allocations with telemetry off and on.
            // Disable idle eviction of the Rgba16Float transient left behind by the Linear test above.
            graph.IdleFrameRetireThreshold = 0;
            tonemap.Operator = TonemapType.Neutral;
            autoExposure.TelemetryEnabled = false;
            for (int i = 0; i < 4; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }

            long beforeNoTelemetry = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - beforeNoTelemetry, Is.Zero);

            autoExposure.TelemetryEnabled = true;
            for (int i = 0; i < 4; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }

            long beforeTelemetry = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 30; ++i)
            {
                ExecuteNoGpuFrame(graph, context);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - beforeTelemetry, Is.Zero);
        }
        finally
        {
            graph.Dispose();
            if (!hdrScene.IsDisposed) hdrScene.Dispose();
            context.Dispose();
        }
    }

    [TestCase(GraphicsBackend.NoGPU)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void Gpu_Convergence_Trimming_TemporalRate_And_ConsumerIntegration(GraphicsBackend backend)
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
            const uint width = 64;
            const uint height = 64;

            using RGNode_AutoExposure ae = new(rendering, b => default)
            {
                AlwaysProducesOutput = true,
            };

            if (backend == GraphicsBackend.NoGPU)
            {
                Texture noGpuTex = CreateHdrTexture(rendering, width, height, TextureUsage.Sampled | TextureUsage.TransferDestination);
                RenderGraph noGpuGraph = new(rendering, new Extent2D(width, height), "AeNoGpu");
                using RGNode_AutoExposure aeNoGpu = new(rendering, b => b.ImportTexture(noGpuTex, "In")) { AlwaysProducesOutput = true };
                try
                {
                    noGpuGraph.Use(aeNoGpu);
                    context.BeginFrame();
                    noGpuGraph.Execute(context, deltaTime: 1f / 60f);
                    context.Submit();
                    Assert.That(aeNoGpu.TryReadState(out _), Is.False);
                }
                finally
                {
                    noGpuGraph.Dispose();
                    noGpuTex.Dispose();
                }
                return;
            }

            // 1. Uniform fields converge to KeyValue / L on first frame.
            VerifyUniformLuminanceConvergence(rendering, context, width, height, 0.05f);
            VerifyUniformLuminanceConvergence(rendering, context, width, height, 1.0f);
            VerifyUniformLuminanceConvergence(rendering, context, width, height, 8.0f);

            // 2. Dark floor + bright sun spike + NaN/Inf pixels trimmed cleanly to midtone 1.0.
            VerifyPercentileTrimmingAndSanitization(rendering, context, width, height);

            // 3. Asymmetric SpeedUp / SpeedDown, dt=0 freeze, Reset() snap, and dt frame-rate independence.
            VerifyTemporalAdaptationAndFramerateIndependence(rendering, context, width, height);

            // 4. End-to-end AutoExposure -> Bloom -> Tonemap + async telemetry readback.
            VerifyBloomAndTonemapIntegrationWithTelemetry(rendering, context, width, height);
        }
        finally
        {
            device.WaitIdle();
            context.Dispose();
            rendering.Dispose();
            device.Destroy();
        }
    }

    private static void VerifyUniformLuminanceConvergence(
        RenderingSystem rendering,
        RenderContext context,
        uint width,
        uint height,
        float luminance)
    {
        Texture input = CreateHdrTexture(rendering, width, height, TextureUsage.Sampled | TextureUsage.TransferDestination);
        UploadSolidHdr(rendering, input, new Vector4(luminance, luminance, luminance, 1.0f), (int)(width * height));

        RenderGraph graph = new(rendering, new Extent2D(width, height), "AeUniform");
        using RGNode_AutoExposure ae = new(rendering, b => b.ImportTexture(input, "Src"))
        {
            AlwaysProducesOutput = true,
            Settings = AutoExposureSettings.Default with
            {
                MinEV = -6.0f,
                MaxEV = 10.0f,
                KeyValue = 0.18f,
                MeteringMode = AutoExposureMeteringMode.Uniform,
                HalfResolution = false,
            },
        };
        try
        {
            graph.Use(ae);
            context.BeginFrame();
            graph.Execute(context, deltaTime: 0.016f);
            context.Submit();
            rendering.WaitIdle();

            Assert.That(ae.TryReadState(out AutoExposureState state), Is.True);
            float expectedExposure = 0.18f / luminance;
            Assert.That(state.Exposure, Is.EqualTo(expectedExposure).Within(expectedExposure * 0.06f),
                $"Uniform luminance {luminance} expected exposure {expectedExposure}.");
            Assert.That(state.AverageLuminance, Is.EqualTo(luminance).Within(luminance * 0.06f));
        }
        finally
        {
            graph.Dispose();
            input.Dispose();
        }
    }

    private static void VerifyPercentileTrimmingAndSanitization(
        RenderingSystem rendering,
        RenderContext context,
        uint width,
        uint height)
    {
        int total = (int)(width * height);
        Vector4[] pixels = new Vector4[total];
        int darkCount = (int)(total * 0.35f);
        int spikeCount = (int)(total * 0.03f);
        for (int i = 0; i < total; ++i)
        {
            if (i < darkCount)
                pixels[i] = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            else if (i < darkCount + spikeCount)
                pixels[i] = new Vector4(100.0f, 100.0f, 100.0f, 1.0f);
            else
                pixels[i] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        }

        pixels[total - 3] = new Vector4(float.NaN, 1.0f, 1.0f, 1.0f);
        pixels[total - 2] = new Vector4(1.0f, float.PositiveInfinity, 1.0f, 1.0f);
        pixels[total - 1] = new Vector4(-4.0f, -2.0f, 0.0f, 1.0f);

        Texture input = CreateHdrTexture(rendering, width, height, TextureUsage.Sampled | TextureUsage.TransferDestination);
        UploadHdrPixels(rendering, input, pixels);

        RenderGraph graph = new(rendering, new Extent2D(width, height), "AeTrim");
        using RGNode_AutoExposure ae = new(rendering, b => b.ImportTexture(input, "Src"))
        {
            AlwaysProducesOutput = true,
            Settings = AutoExposureSettings.Default with
            {
                MeteringMode = AutoExposureMeteringMode.Uniform,
                HalfResolution = false,
                IgnoreDarkFloor = true,
                LowPercent = 0.40f,
                HighPercent = 0.90f,
            },
        };
        try
        {
            graph.Use(ae);
            context.BeginFrame();
            graph.Execute(context, deltaTime: 0.016f);
            context.Submit();
            rendering.WaitIdle();

            Assert.That(ae.TryReadState(out AutoExposureState state), Is.True);
            Assert.That(float.IsFinite(state.Exposure) && float.IsFinite(state.AdaptedEV), Is.True);
            Assert.That(state.AverageLuminance, Is.EqualTo(1.0f).Within(0.08f));
            Assert.That(state.Exposure, Is.EqualTo(0.18f).Within(0.02f));
        }
        finally
        {
            graph.Dispose();
            input.Dispose();
        }
    }

    private static void VerifyTemporalAdaptationAndFramerateIndependence(
        RenderingSystem rendering,
        RenderContext context,
        uint width,
        uint height)
    {
        Texture input = CreateHdrTexture(rendering, width, height, TextureUsage.Sampled | TextureUsage.TransferDestination);
        RenderGraph graph = new(rendering, new Extent2D(width, height), "AeTemporal");
        using RGNode_AutoExposure ae = new(rendering, b => b.ImportTexture(input, "Src"))
        {
            AlwaysProducesOutput = true,
            Settings = AutoExposureSettings.Default with
            {
                MinEV = -6.0f,
                MaxEV = 10.0f,
                SpeedUp = 4.0f,
                SpeedDown = 1.0f,
                MeteringMode = AutoExposureMeteringMode.Uniform,
                HalfResolution = false,
            },
        };
        try
        {
            graph.Use(ae);
            int count = (int)(width * height);

            // Snap to dim baseline (0.25) -> jump to bright (4.0) with 2 steps of dt=0.10.
            UploadSolidHdr(rendering, input, new Vector4(0.25f, 0.25f, 0.25f, 1.0f), count);
            StepFrame(graph, context, rendering, 0.016f);
            Assert.That(ae.TryReadState(out AutoExposureState baseState), Is.True);

            UploadSolidHdr(rendering, input, new Vector4(4.0f, 4.0f, 4.0f, 1.0f), count);
            StepFrame(graph, context, rendering, 0.10f);
            StepFrame(graph, context, rendering, 0.10f);
            Assert.That(ae.TryReadState(out AutoExposureState twoStepState), Is.True);

            // Repeat from same dim baseline with 1 step of dt=0.20 (framerate independence).
            ae.Reset();
            UploadSolidHdr(rendering, input, new Vector4(0.25f, 0.25f, 0.25f, 1.0f), count);
            StepFrame(graph, context, rendering, 0.016f);
            UploadSolidHdr(rendering, input, new Vector4(4.0f, 4.0f, 4.0f, 1.0f), count);
            StepFrame(graph, context, rendering, 0.20f);
            Assert.That(ae.TryReadState(out AutoExposureState oneStepState), Is.True);

            Assert.That(twoStepState.AdaptedEV, Is.EqualTo(oneStepState.AdaptedEV).Within(0.02f),
                "2 steps of dt=0.10 must match 1 step of dt=0.20.");

            float upDelta = MathF.Abs(oneStepState.AdaptedEV - baseState.AdaptedEV);

            // dt=0 must freeze adapted EV completely.
            StepFrame(graph, context, rendering, 0.0f);
            Assert.That(ae.TryReadState(out AutoExposureState frozenState), Is.True);
            Assert.That(frozenState.AdaptedEV, Is.EqualTo(oneStepState.AdaptedEV).Within(1e-5f));

            // SpeedDown (4.0 -> 0.25 over dt=0.20) moves less than SpeedUp over the same interval.
            ae.Reset();
            UploadSolidHdr(rendering, input, new Vector4(4.0f, 4.0f, 4.0f, 1.0f), count);
            StepFrame(graph, context, rendering, 0.016f);
            Assert.That(ae.TryReadState(out AutoExposureState brightStart), Is.True);

            UploadSolidHdr(rendering, input, new Vector4(0.25f, 0.25f, 0.25f, 1.0f), count);
            StepFrame(graph, context, rendering, 0.20f);
            Assert.That(ae.TryReadState(out AutoExposureState downStepState), Is.True);
            float downDelta = MathF.Abs(downStepState.AdaptedEV - brightStart.AdaptedEV);
            Assert.That(downDelta, Is.LessThan(upDelta * 0.6f), "SpeedDown=1 must adapt slower than SpeedUp=4.");

            // Reset() snaps immediately on next frame.
            ae.Reset();
            StepFrame(graph, context, rendering, 0.016f);
            Assert.That(ae.TryReadState(out AutoExposureState snappedState), Is.True);
            Assert.That(snappedState.AdaptedEV, Is.EqualTo(snappedState.TargetEV).Within(1e-4f));
        }
        finally
        {
            graph.Dispose();
            input.Dispose();
        }
    }

    private static void VerifyBloomAndTonemapIntegrationWithTelemetry(
        RenderingSystem rendering,
        RenderContext context,
        uint width,
        uint height)
    {
        int total = (int)(width * height);
        Vector4[] pixels = new Vector4[total];
        for (int i = 0; i < total; ++i)
        {
            pixels[i] = new Vector4(0.05f, 0.05f, 0.05f, 1.0f);
        }
        // Small sub-1.0 spot (0.6 < Bloom.Threshold=1.0 in raw HDR, but > 1.0 after AE exposure=3.6 -> 2.16!).
        for (int y = 28; y < 36; ++y)
        {
            for (int x = 28; x < 36; ++x)
            {
                pixels[y * (int)width + x] = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
            }
        }

        Texture src = CreateHdrTexture(rendering, width, height,
            TextureUsage.ColorAttachment | TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.TransferDestination);
        Texture dstNoAe = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Storage | TextureUsage.TransferSource,
        });
        Texture dstWithAe = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Storage | TextureUsage.TransferSource,
        });

        RenderGraph graphNoAe = new(rendering, new Extent2D(width, height), "NoAeBloomTonemap");
        RenderGraph graphAe = new(rendering, new Extent2D(width, height), "WithAeBloomTonemap");
        RenderChain chainAe = new();
        using RGNode_AutoExposure ae = new(rendering, chainAe)
        {
            TelemetryEnabled = true,
            Settings = AutoExposureSettings.Default with
            {
                MinEV = -6.0f,
                MaxEV = 10.0f,
                MeteringMode = AutoExposureMeteringMode.Uniform,
                HalfResolution = false,
            },
        };

        try
        {
            // Baseline without AutoExposure: dim 0.05 background and 0.6 spot stay below bloom threshold 1.0.
            UploadHdrPixels(rendering, src, pixels);
            RGNode_Bloom bloomNoAe = new(rendering, b => b.ImportTexture(src, "NoAeSrc"))
            {
                Threshold = 1.0f,
                Intensity = 1.5f,
                CompositeMode = BloomCompositeMode.SeparateTargetCompute,
            };
            RenderChain chainNoAe = new();
            graphNoAe.Use(new LambdaNode(b =>
            {
                RGTexture h = b.ImportTexture(src, "NoAeSrc");
                chainNoAe.Reset(h);
            }));
            RGNode_Bloom bloomNoAeChain = new(rendering, chainNoAe)
            {
                Threshold = 1.0f,
                Intensity = 1.5f,
                CompositeMode = BloomCompositeMode.SeparateTargetCompute,
            };
            RGNode_Tonemap tonemapNoAe = new(
                rendering,
                _ => chainNoAe.Current,
                b => b.ImportTexture(dstNoAe, "NoAeOut", RGUsage.TransferRead))
            {
                Operator = TonemapType.ACES,
                AlwaysProducesOutput = true,
            };
            graphNoAe.Use(bloomNoAeChain).Use(tonemapNoAe);
            _ = bloomNoAe;

            StepFrame(graphNoAe, context, rendering, 0.016f);
            byte[] noAeBytes = new byte[width * height * 4];
            rendering.ReadTexture(dstNoAe, noAeBytes);

            // With AutoExposure: exposure boosts 0.05 -> middle gray and lifts 0.6 spot above threshold 1.0 -> blooms onto neighbors!
            UploadHdrPixels(rendering, src, pixels);
            graphAe.Use(new LambdaNode(b =>
            {
                RGTexture h = b.ImportTexture(src, "AeSrc");
                chainAe.Reset(h);
            }));
            RGNode_Bloom bloomAe = new(rendering, chainAe)
            {
                Threshold = 1.0f,
                Intensity = 1.5f,
                CompositeMode = BloomCompositeMode.SeparateTargetCompute,
                ExposureProvider = _ => ae.OutputBuffer,
            };
            RGNode_Tonemap tonemapAe = new(
                rendering,
                _ => chainAe.Current,
                b => b.ImportTexture(dstWithAe, "AeOut", RGUsage.TransferRead))
            {
                Operator = TonemapType.ACES,
                ExposureProvider = _ => ae.OutputBuffer,
                AlwaysProducesOutput = true,
            };
            graphAe.Use(ae).Use(bloomAe).Use(tonemapAe);

            // Execute 3 frames so async 2-slot telemetry readback completes and populates HasTelemetry/LastHistogram.
            StepFrame(graphAe, context, rendering, 0.016f);
            StepFrame(graphAe, context, rendering, 0.016f);
            StepFrame(graphAe, context, rendering, 0.016f);

            byte[] aeBytes = new byte[width * height * 4];
            rendering.ReadTexture(dstWithAe, aeBytes);

            // Corner pixel (2,2) brightens via AE Tonemap, and halo pixel (24,32) outside spot gains bloom energy.
            int cornerIdx = (2 * (int)width + 2) * 4;
            int haloIdx = (32 * (int)width + 24) * 4;
            Assert.That(aeBytes[cornerIdx], Is.GreaterThan(noAeBytes[cornerIdx] + 20),
                "AutoExposure should brighten dim scene background.");
            Assert.That(aeBytes[haloIdx], Is.GreaterThan(aeBytes[cornerIdx] + 4),
                "Exposure-aware bloom should extract the 0.6 spot once exposed above threshold and spill halo onto (24,32).");

            Assert.That(ae.HasTelemetry, Is.True, "Async telemetry should be populated after completed frames.");
            Assert.That(ae.LastState.Initialized, Is.EqualTo(1u));
            Assert.That(ae.LastHistogram.Length, Is.EqualTo(RGNode_AutoExposure.BinCount));
            ulong histSum = 0;
            for (int b = 0; b < ae.LastHistogram.Length; ++b)
            {
                histSum += ae.LastHistogram[b];
            }
            Assert.That(histSum, Is.GreaterThan(0UL));
        }
        finally
        {
            graphAe.Dispose();
            graphNoAe.Dispose();
            dstWithAe.Dispose();
            dstNoAe.Dispose();
            src.Dispose();
        }
    }

    private static void StepFrame(RenderGraph graph, RenderContext context, RenderingSystem rendering, float dt)
    {
        context.BeginFrame();
        graph.Execute(context, deltaTime: dt);
        context.Submit();
        rendering.WaitIdle();
    }

    private static void VerifyTargetLayout<TRoot>(
        ShaderCompiler compiler,
        string moduleName,
        string rootStructName)
        where TRoot : unmanaged
    {
        CompiledShader compiled = compiler.CompileSource(
            moduleName,
            moduleName + ".slang",
            LoadEmbeddedShader(moduleName + ".slang"),
            ShaderPipelineKind.Compute);

        Assert.That(compiled.Reflection.Root, Is.Not.Null, $"{moduleName} must reflect a [GPURoot] layout on {compiler.Target}.");
        Assert.That(compiled.Reflection.Root!.SizeBytes, Is.EqualTo((uint)sizeof(TRoot)),
            $"{moduleName} root byte size mismatch on {compiler.Target}.");
        Assert.That(compiled.Reflection.MatchesStructLayout<TRoot>(rootStructName), Is.True,
            $"{moduleName} root field layout mismatch with {typeof(TRoot).Name} on {compiler.Target}.");
    }

    private static void ExecuteNoGpuFrame(RenderGraph graph, RenderContext context)
    {
        context.BeginFrame();
        graph.Execute(context, deltaTime: 1.0f / 60.0f);
        context.Submit();
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

    private static void UploadSolidHdr(RenderingSystem rendering, Texture texture, Vector4 color, int count)
    {
        Vector4[] pixels = new Vector4[count];
        Array.Fill(pixels, color);
        UploadHdrPixels(rendering, texture, pixels);
    }

    private static void UploadHdrPixels(RenderingSystem rendering, Texture texture, Vector4[] pixels)
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

    private static string LoadEmbeddedShader(string logicalName)
    {
        Assembly assembly = typeof(RGNode_AutoExposure).Assembly;
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
