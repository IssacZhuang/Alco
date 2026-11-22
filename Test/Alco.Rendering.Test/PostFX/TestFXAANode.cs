using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;
using Buffer = Alco.Graphics.Buffer;

namespace Alco.Rendering.Test;

/// <summary>
/// CPU RenderGraph planning/routing/zero-GC tests and multi-backend GPU quality/depth tests for <see cref="RGNode_FXAA"/>.
/// </summary>
[TestFixture]
public unsafe class TestFXAANode
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ExpectedFxaaRoot
    {
        public Vector2 InvSourceSize;
        public uint SourceSizeX;
        public uint SourceSizeY;
        public uint Source;
        public uint Depth;
        public uint Dest;
        public uint IsPassthrough;
        public float EdgeThreshold;
        public float EdgeThresholdMin;
        public float SubpixQuality;
        public float DepthThresholdMin;
        public float DepthThresholdRel;
        public float ColorVisibilityFloor;
    }

    [TestCase(ShaderTarget.SpirvCompact)]
    [TestCase(ShaderTarget.Spirv)]
    [TestCase(ShaderTarget.MetalSource)]
    [TestCase(ShaderTarget.Dxil)]
    public void FxaaShaders_CompileAndMatchRootLayouts_AcrossAllTargetsAndAxes(ShaderTarget target)
    {
        if (!ShaderCompiler.IsTargetSupported(target))
        {
            Assert.Ignore($"Shader target {target} is unsupported on this host.");
            return;
        }

        using ShaderCompiler compiler = new(target);
        compiler.LoadModuleFromSource(
            "AlcoRendering_FXAA",
            "AlcoRendering_FXAA.slang",
            LoadEmbeddedShader("AlcoRendering_FXAA.slang"));

        ShaderModule computeModule = compiler.LoadModuleFromSource(
            "FXAACompute",
            "FXAACompute.slang",
            LoadEmbeddedShader("FXAACompute.slang"));
        ShaderModule graphicsModule = compiler.LoadModuleFromSource(
            "FXAAGraphics",
            "FXAAGraphics.slang",
            LoadEmbeddedShader("FXAAGraphics.slang"));

        // Verify every Quality x Mode combination (Gamma) and every ColorSpace x Mode combination (Medium).
        for (int q = 0; q <= (int)FXAAQuality.Ultra; ++q)
        {
            for (int m = 0; m <= (int)FXAAMode.DepthOnly; ++m)
            {
                VerifyModuleLayout(computeModule, ShaderPipelineKind.Compute, q, m, (int)FXAAColorSpace.Gamma, target);
                VerifyModuleLayout(graphicsModule, ShaderPipelineKind.Graphics, q, m, (int)FXAAColorSpace.Gamma, target);
            }
        }

        for (int c = (int)FXAAColorSpace.Linear; c <= (int)FXAAColorSpace.Hdr; ++c)
        {
            for (int m = 0; m <= (int)FXAAMode.DepthOnly; ++m)
            {
                VerifyModuleLayout(computeModule, ShaderPipelineKind.Compute, (int)FXAAQuality.Medium, m, c, target);
                VerifyModuleLayout(graphicsModule, ShaderPipelineKind.Graphics, (int)FXAAQuality.Medium, m, c, target);
            }
        }
    }

    [Test]
    public void RenderGraph_ChainRouting_DepthLiveness_Validation_And_ZeroSteadyStateAllocations()
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.NoGPU);
        RenderContext context = new(host.RenderingSystem);
        RenderGraph graph = new(host.RenderingSystem, new Extent2D(1919, 1079), "FxaaNoGpuGraph");
        RenderChain chain = new();

        Texture sceneColor = host.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1919, 1079, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
        });
        Texture sceneDepth = host.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(1919, 1079, 1u),
            Format = Format.D32Float,
            Usage = TextureUsage.DepthStencilAttachment | TextureUsage.Sampled,
        });
        Texture srgbColor = host.RenderingSystem.CreateTexture(new TextureDesc
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
            // 1. Missing depth provider rejects depth-aware modes immediately.
            RGNode_FXAA noDepthFxaa = new(host.RenderingSystem, chain);
            Assert.Throws<InvalidOperationException>(() => noDepthFxaa.Mode = FXAAMode.DepthAssisted);
            Assert.Throws<InvalidOperationException>(() => noDepthFxaa.Mode = FXAAMode.DepthOnly);

            // 2. Chain mode with depth provider: Depth pass stays alive in DepthOnly/DepthAssisted and is culled in Luma.
            RGTexture importedScene = graph.ImportTexture(sceneColor, "SceneLDR", finalUsage: RGUsage.PixelRead);
            RGTexture importedDepth = graph.ImportTexture(sceneDepth, "SceneDepth");
            bool provideValidDepth = true;
            RGTexture capturedChainEnd = default;

            RGNode_FXAA fxaa = new(
                host.RenderingSystem,
                chain,
                _ => provideValidDepth ? importedDepth : default);
            fxaa.Mode = FXAAMode.DepthOnly;

            graph.Use(new LambdaNode(b =>
            {
                chain.Reset(importedScene);
                RenderGraphPassBuilder colorPass = b.AddPass("SceneColor");
                colorPass.Write(importedScene, RGUsage.ColorAttachmentWrite);
                RenderGraphPassBuilder depthPass = b.AddPass("SceneDepthPass");
                depthPass.Write(importedDepth, RGUsage.DepthStencilWrite);
            }));
            graph.Use(fxaa);
            graph.Use(new LambdaNode(b =>
            {
                capturedChainEnd = chain.Current;
                RenderGraphPassBuilder consumePass = b.AddPass("Consume");
                consumePass.Read(capturedChainEnd, RGUsage.PixelRead);
                consumePass.ProducesOutput();
            }));

            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedChainEnd, Is.Not.EqualTo(importedScene));
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(4));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(4));
            Assert.That(graph.Statistics.CulledPassCount, Is.Zero);
            Assert.That(graph.GetResolvedTextureUsage(capturedChainEnd).HasFlag(TextureUsage.Storage), Is.True,
                "Auto execution on chain transients should infer Compute Storage usage.");

            // Switching to Luma makes SceneDepthPass unreferenced and automatically culled.
            fxaa.Mode = FXAAMode.Luma;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(4));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(3));
            Assert.That(graph.Statistics.CulledPassCount, Is.EqualTo(1));

            // Dynamic invalid depth handle on a frame degrades gracefully without throwing.
            provideValidDepth = false;
            fxaa.Mode = FXAAMode.DepthAssisted;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(3));

            fxaa.Mode = FXAAMode.DepthOnly;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedChainEnd, Is.EqualTo(importedScene));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(2));

            // Chain Bypass skips FXAA pass cleanly when not AlwaysProducesOutput.
            provideValidDepth = true;
            fxaa.Bypass = true;
            ExecuteNoGpuFrame(graph, context);
            Assert.That(capturedChainEnd, Is.EqualTo(importedScene));
            Assert.That(graph.Statistics.TotalPassCount, Is.EqualTo(3));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(2));

            // 3. Steady-state zero GC allocations on Compute chain path (with DepthOnly active).
            fxaa.Bypass = false;
            fxaa.Mode = FXAAMode.DepthOnly;
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
                $"Steady-state Compute FXAA must allocate 0 managed bytes (allocated={computeAlloc}).");

            // 4. Explicit output routing: sRGB chain and ColorAttachment-only outputs route to Graphics automatically, and allocate 0 bytes.
            RenderGraph graphicsGraph = new(host.RenderingSystem, new Extent2D(1919, 1079), "FxaaGraphicsRouting");
            try
            {
                RGTexture srgbHandle = graphicsGraph.ImportTexture(srgbColor, "SrgbScene");
                RGTexture colorOnlyHandle = graphicsGraph.ImportTexture(colorOnlyOutput, "ColorOnlyOut", RGUsage.TransferRead);
                RGTexture depthInGfx = graphicsGraph.ImportTexture(sceneDepth, "DepthInGfx");
                RGNode_FXAA gfxWithLocalDepth = new(
                    host.RenderingSystem,
                    _ => srgbHandle,
                    _ => colorOnlyHandle,
                    _ => depthInGfx)
                {
                    Mode = FXAAMode.DepthAssisted,
                    AlwaysProducesOutput = true,
                };
                graphicsGraph.Use(gfxWithLocalDepth);

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
                    $"Steady-state Graphics FXAA must allocate 0 managed bytes (allocated={gfxAlloc}).");

                // Verify same-texture in-place throws explicitly.
                RenderGraph badInPlace = new(host.RenderingSystem, new Extent2D(64, 64), "BadInPlace");
                try
                {
                    RGTexture h = badInPlace.ImportTexture(sceneColor, "Same");
                    badInPlace.Use(new RGNode_FXAA(host.RenderingSystem, _ => h, _ => h) { AlwaysProducesOutput = true });
                    Assert.Throws<InvalidOperationException>(() => badInPlace.CompileDryRun());
                }
                finally
                {
                    badInPlace.Destroy();
                }
            }
            finally
            {
                graphicsGraph.Destroy();
            }
        }
        finally
        {
            graph.Destroy();
            colorOnlyOutput.Destroy();
            srgbColor.Destroy();
            sceneDepth.Destroy();
            sceneColor.Destroy();
            context.Destroy();
        }
    }

    [TestCase(GraphicsBackend.NoGPU)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Vulkan)]
    [TestCase(GraphicsBackend.Dx12)]
    public void Gpu_LumaDepthColorSpaceAndExecutionPaths_WorkAcrossBackends(GraphicsBackend backend)
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
            if (backend == GraphicsBackend.NoGPU)
            {
                // Verify both execution paths and passthrough record cleanly on NoGPU.
                byte[] dummyLdr = BuildStaircaseTestImage(129, 65);
                _ = ExecuteRgba8Fxaa(rendering, context, dummyLdr, null, 129, 65, Format.D32Float,
                    FXAAQuality.High, FXAAMode.Luma, FXAAColorSpace.Gamma, FXAAExecutionMode.Compute, bypass: false, readback: false);
                _ = ExecuteRgba8Fxaa(rendering, context, dummyLdr, null, 129, 65, Format.D32Float,
                    FXAAQuality.High, FXAAMode.Luma, FXAAColorSpace.Gamma, FXAAExecutionMode.Graphics, bypass: false, readback: false);
                _ = ExecuteRgba8Fxaa(rendering, context, dummyLdr, null, 129, 65, Format.D32Float,
                    FXAAQuality.Medium, FXAAMode.Luma, FXAAColorSpace.Gamma, FXAAExecutionMode.Auto, bypass: true, readback: false);
                return;
            }

            // 1. Odd resolution 129x65: staircase across 16x16 LDS tile borders, blue-channel edge, flat interior + alpha preservation, Compute vs Graphics parity.
            const uint w1 = 129;
            const uint h1 = 65;
            byte[] ldrInput = BuildStaircaseTestImage((int)w1, (int)h1);

            byte[] computeOut = ExecuteRgba8Fxaa(rendering, context, ldrInput, null, w1, h1, Format.D32Float,
                FXAAQuality.High, FXAAMode.Luma, FXAAColorSpace.Gamma, FXAAExecutionMode.Compute, bypass: false);
            byte[] graphicsOut = ExecuteRgba8Fxaa(rendering, context, ldrInput, null, w1, h1, Format.D32Float,
                FXAAQuality.High, FXAAMode.Luma, FXAAColorSpace.Gamma, FXAAExecutionMode.Graphics, bypass: false);

            AssertComputeAndGraphicsMatch(computeOut, graphicsOut, (int)w1, (int)h1, maxChannelDiff: 2);
            AssertAlphaPreserved(ldrInput, computeOut, (int)w1, (int)h1, "LumaComputeAlpha");
            AssertAlphaPreserved(ldrInput, graphicsOut, (int)w1, (int)h1, "LumaGraphicsAlpha");

            // Flat interior region (around (5, 55)) must stay bit-exact.
            AssertPixelEquals(ldrInput, computeOut, (int)w1, 5, 55, "FlatInterior");

            // White-on-black shallow staircase crossing x=16 / y=16 tile boundary must be smoothed (intermediate grayscale values produced).
            AssertRegionSmoothed(ldrInput, computeOut, (int)w1, x0: 8, y0: 14, x1: 26, y1: 20, channel: 0, "TileBoundaryStaircase");

            // Pure blue (0, 0, 255) vs black staircase must also be detected and smoothed (blue channel sensitivity).
            AssertRegionSmoothed(ldrInput, computeOut, (int)w1, x0: 40, y0: 40, x1: 60, y1: 48, channel: 2, "BlueChannelStaircase");

            // 2. Depth-aware modes (D32Float): sloped planar ramp must NOT trigger false AA, while low-luma silhouette IS rescued.
            const uint w2 = 128;
            const uint h2 = 64;
            BuildDepthSceneTest(
                (int)w2, (int)h2,
                out byte[] depthColorInput,
                out float[] depthValues);

            byte[] lumaOnlyHighGate = ExecuteRgba8Fxaa(rendering, context, depthColorInput, depthValues, w2, h2, Format.D32Float,
                FXAAQuality.Medium, FXAAMode.Luma, FXAAColorSpace.Gamma, FXAAExecutionMode.Compute, bypass: false, threshold: 0.333f);
            byte[] depthOnlyOut = ExecuteRgba8Fxaa(rendering, context, depthColorInput, depthValues, w2, h2, Format.D32Float,
                FXAAQuality.Medium, FXAAMode.DepthOnly, FXAAColorSpace.Gamma, FXAAExecutionMode.Compute, bypass: false, threshold: 0.333f);
            byte[] depthAssistedGfxOut = ExecuteRgba8Fxaa(rendering, context, depthColorInput, depthValues, w2, h2, Format.D32Float,
                FXAAQuality.Medium, FXAAMode.DepthAssisted, FXAAColorSpace.Gamma, FXAAExecutionMode.Graphics, bypass: false, threshold: 0.333f);

            // Sloped plane striped texture (x in [4..56], y in [8..56]): DepthOnly leaves stripes untouched (second-order depth continuity).
            AssertRegionBitExact(depthColorInput, depthOnlyOut, (int)w2, x0: 4, y0: 8, x1: 56, y1: 56, "SlopedPlanePreserved");

            // Low-contrast geometric silhouette on the right half (x in [72..120], y in [20..44]):
            // high luma threshold ignores it (lumaOnlyHighGate == input), while DepthOnly and DepthAssisted rescue & smooth it!
            AssertRegionBitExact(depthColorInput, lumaOnlyHighGate, (int)w2, x0: 72, y0: 20, x1: 120, y1: 44, "LumaRejectsLowContrastSilhouette");
            AssertRegionSmoothed(depthColorInput, depthOnlyOut, (int)w2, x0: 72, y0: 20, x1: 120, y1: 44, channel: 1, "DepthOnlyRescuesSilhouette");
            AssertRegionSmoothed(depthColorInput, depthAssistedGfxOut, (int)w2, x0: 72, y0: 20, x1: 120, y1: 44, channel: 1, "DepthAssistedGraphicsRescuesSilhouette");

            // Also verify packed Depth24+Stencil8 sampling when supported by the backend (engine default depth-stencil format).
            if (device.SupportsTextureFormat(Format.D24UnormS8Uint, TextureUsage.DepthStencilAttachment | TextureUsage.Sampled))
            {
                byte[] d24Out = ExecuteRgba8Fxaa(rendering, context, depthColorInput, depthValues, w2, h2, Format.D24UnormS8Uint,
                    FXAAQuality.Medium, FXAAMode.DepthOnly, FXAAColorSpace.Gamma, FXAAExecutionMode.Compute, bypass: false, threshold: 0.333f);
                AssertRegionBitExact(depthColorInput, d24Out, (int)w2, x0: 4, y0: 8, x1: 56, y1: 56, "D24S8SlopedPlanePreserved");
                AssertRegionSmoothed(depthColorInput, d24Out, (int)w2, x0: 72, y0: 20, x1: 120, y1: 44, channel: 1, "D24S8DepthOnlyRescuesSilhouette");
            }

            // 3. Linear & HDR color spaces on Rgba16Float input.
            VerifyHdrColorSpaces(rendering, context);

            // 4. Bypass passthrough directly to builder.Output (non-Storage target) via Graphics fallback.
            VerifyBypassPassthroughToOutput(rendering, context, ldrInput, w1, h1);
        }
        finally
        {
            device.WaitIdle();
            context.Destroy();
            rendering.Destroy();
            device.Destroy();
        }
    }

    private static void VerifyModuleLayout(
        ShaderModule module,
        ShaderPipelineKind kind,
        int quality,
        int mode,
        int colorSpace,
        ShaderTarget target)
    {
        CompiledShader compiled = module.Compile(kind, new ShaderCompileOptions
        {
            SpecializationArgs =
            [
                quality.ToString(),
                mode.ToString(),
                colorSpace.ToString(),
            ],
        });

        Assert.That(compiled.Reflection.Root, Is.Not.Null,
            $"{module.Name}<{quality},{mode},{colorSpace}> must reflect FxaaRoot on {target}.");
        Assert.That(compiled.Reflection.Root!.SizeBytes, Is.EqualTo((uint)sizeof(ExpectedFxaaRoot)),
            $"{module.Name}<{quality},{mode},{colorSpace}> root size mismatch on {target}.");
        Assert.That(compiled.Reflection.MatchesStructLayout<ExpectedFxaaRoot>("FxaaRoot"), Is.True,
            $"{module.Name}<{quality},{mode},{colorSpace}> root field layout mismatch on {target}.");
    }

    private static void ExecuteNoGpuFrame(RenderGraph graph, RenderContext context)
    {
        context.BeginFrame();
        graph.Execute(context);
        context.Submit().Wait();
    }

    private static byte[] ExecuteRgba8Fxaa(
        RenderingSystem rendering,
        RenderContext context,
        byte[] colorInput,
        float[]? depthInput,
        uint width,
        uint height,
        Format depthFormat,
        FXAAQuality quality,
        FXAAMode mode,
        FXAAColorSpace colorSpace,
        FXAAExecutionMode executionMode,
        bool bypass,
        float threshold = 0.125f,
        bool readback = true)
    {
        Texture srcColor = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Texture dstColor = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Storage | TextureUsage.TransferSource,
        });
        Texture? srcDepth = null;
        if (depthInput != null)
        {
            srcDepth = rendering.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(width, height, 1u),
                Format = depthFormat,
                Usage = TextureUsage.DepthStencilAttachment | TextureUsage.Sampled,
            });
        }

        RenderGraph graph = new(rendering, new Extent2D(width, height), "FxaaGpuTest");
        try
        {
            rendering.UploadTexture(srcColor, colorInput);
            if (srcDepth != null && depthInput != null)
            {
                UploadDepthViaPass(rendering, context, srcDepth, depthInput, width, height);
            }

            RGNode_FXAA fxaa = new(
                rendering,
                b => b.ImportTexture(srcColor, "FxaaSrc"),
                b => b.ImportTexture(dstColor, "FxaaDst", RGUsage.TransferRead),
                srcDepth != null ? b => b.ImportTexture(srcDepth, "FxaaDepth") : null)
            {
                Quality = quality,
                Mode = mode,
                ColorSpace = colorSpace,
                ExecutionMode = executionMode,
                Threshold = threshold,
                Bypass = bypass,
                AlwaysProducesOutput = true,
            };
            graph.Use(fxaa);

            context.BeginFrame();
            graph.Execute(context);
            context.Submit().Wait();

            if (!readback)
            {
                return [];
            }

            byte[] result = new byte[width * height * 4];
            rendering.ReadTexture(dstColor, result);
            return result;
        }
        finally
        {
            graph.Destroy();
            if (srcDepth != null && !srcDepth.IsDestroyed)
            {
                srcDepth.Destroy();
            }
            if (!dstColor.IsDestroyed)
            {
                dstColor.Destroy();
            }
            if (!srcColor.IsDestroyed)
            {
                srcColor.Destroy();
            }
        }
    }

    private static void UploadDepthViaPass(
        RenderingSystem rendering,
        RenderContext context,
        Texture depthTexture,
        float[] depthValues,
        uint width,
        uint height)
    {
        // Populate depth attachment cleanly across all backends via a fullscreen shader writing SV_Depth.
        const string depthWriteSource = """
            #language slang 2025
            module TestFxaaDepthSeed;

            import Core;

            [GPURoot]
            struct DepthSeedRoot
            {
                GPUBufferView DepthValues;
                uint Width;
                uint Pad;
            };

            struct V2F
            {
                float4 Position : SV_Position;
            };

            [shader("vertex")]
            V2F MainVS(uint vertexId : SV_VertexID)
            {
                float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
                V2F o;
                o.Position = float4(uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
                return o;
            }

            [shader("pixel")]
            float MainPS(V2F input) : SV_Depth
            {
                DepthSeedRoot root = gpu_load_root<DepthSeedRoot>();
                uint x = uint(input.Position.x);
                uint y = uint(input.Position.y);
                return gpu_load<float>(root.DepthValues, y * root.Width + x);
            }
            """;

        Buffer depthBuffer = rendering.CreateBuffer<float>(depthValues);
        Shader shader = rendering.Shaders.GetShaderFromSource(
            "TestFxaaDepthSeed", "TestFxaaDepthSeed.slang", depthWriteSource, ShaderKind.Graphics);
        Material material = shader.CreateMaterial("seed_depth");
        material.Rasterization = RasterizationState.CullNone;

        try
        {
            context.BeginFrame();
            GPURange root = context.AllocateRoot(new DepthSeedRoot
            {
                DepthValues = depthBuffer.View,
                Width = width,
            });
            RenderingDesc desc = new()
            {
                Depth = new DepthAttachment
                {
                    RenderView = depthTexture.RenderView,
                    Load = LoadOp.Clear,
                    Store = StoreOp.Store,
                    Clear = 1.0f,
                },
            };
            using (RenderContext.RenderScope render = context.BeginRenderPass(desc, ReadOnlySpan<Format>.Empty, depthTexture.Format))
            {
                render.SetViewport(new Viewport { Width = width, Height = height });
                render.SetScissor(new Scissor { Width = width, Height = height });
                render.Draw(material, root, new DepthStencilState { DepthTest = true, DepthWrite = true, DepthCompare = CompareOp.Always }, 3u);
            }
            context.Barrier(Stage.DepthStencilTests, Access.DepthStencilWrite, Stage.Compute | Stage.Pixel, Access.ShaderRead);
            context.Submit().Wait();
        }
        finally
        {
            depthBuffer.Destroy();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DepthSeedRoot
    {
        public GPUBufferView DepthValues;
        public uint Width;
        public uint Pad;
    }

    private static void VerifyHdrColorSpaces(RenderingSystem rendering, RenderContext context)
    {
        const uint width = 64;
        const uint height = 64;
        Vector4[] hdrPixels = new Vector4[width * height];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                // 1:4 shallow staircase separating dark 0.05 from bright HDR 10.0.
                int edgeY = 24 + (x / 4);
                bool bright = y >= edgeY;
                hdrPixels[y * width + x] = bright
                    ? new Vector4(10.0f, 8.0f, 6.0f, 0.75f)
                    : new Vector4(0.05f, 0.05f, 0.05f, 0.25f);
            }
        }

        Texture src = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba16Float,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Texture dst = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba16Float,
            Usage = TextureUsage.ColorAttachment | TextureUsage.Storage | TextureUsage.TransferSource,
        });
        RenderGraph graph = new(rendering, new Extent2D(width, height), "FxaaHdrTest");
        try
        {
            UploadRgba16Float(rendering, src, hdrPixels);
            RGNode_FXAA fxaa = new(
                rendering,
                b => b.ImportTexture(src, "HdrSrc"),
                b => b.ImportTexture(dst, "HdrDst", RGUsage.TransferRead))
            {
                ColorSpace = FXAAColorSpace.Hdr,
                Quality = FXAAQuality.High,
                AlwaysProducesOutput = true,
            };
            graph.Use(fxaa);

            context.BeginFrame();
            graph.Execute(context);
            context.Submit().Wait();

            Vector4[] hdrOut = ReadbackRgba16Float(rendering, dst, (int)width, (int)height);
            int smoothedCount = 0;
            for (int y = 20; y < 44; ++y)
            {
                for (int x = 4; x < 60; ++x)
                {
                    int idx = y * (int)width + x;
                    Assert.That(float.IsFinite(hdrOut[idx].X) && float.IsFinite(hdrOut[idx].Y) && float.IsFinite(hdrOut[idx].Z), Is.True,
                        "HDR FXAA produced non-finite pixel.");
                    Assert.That(MathF.Abs(hdrOut[idx].W - hdrPixels[idx].W), Is.LessThanOrEqualTo(0.005f),
                        "HDR FXAA did not preserve alpha.");
                    if (hdrOut[idx].X > 0.1f && hdrOut[idx].X < 9.5f)
                    {
                        smoothedCount++;
                    }
                }
            }

            Assert.That(smoothedCount, Is.GreaterThan(12), "HDR FXAA should produce smooth intermediate transitions along the HDR staircase.");
        }
        finally
        {
            graph.Destroy();
            if (!dst.IsDestroyed) dst.Destroy();
            if (!src.IsDestroyed) src.Destroy();
        }
    }

    private static void VerifyBypassPassthroughToOutput(
        RenderingSystem rendering,
        RenderContext context,
        byte[] input,
        uint width,
        uint height)
    {
        Texture src = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
        });
        Texture output = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(width, height, 1u),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource, // No Storage: forces Graphics fallback.
        });
        RenderGraph graph = new(rendering, new Extent2D(width, height), "FxaaBypassOutput");
        try
        {
            rendering.UploadTexture(src, input);
            RGNode_FXAA fxaa = new(rendering, b => b.ImportTexture(src, "BypassSrc"), b => b.Output)
            {
                Bypass = true,
                ExecutionMode = FXAAExecutionMode.Auto,
                AlwaysProducesOutput = true,
            };
            graph.Use(fxaa);

            context.BeginFrame();
            graph.Execute(context, RenderGraphTarget.FromTexture(output), outputFinalUsage: RGUsage.TransferRead);
            context.Submit().Wait();

            byte[] actual = new byte[width * height * 4];
            rendering.ReadTexture(output, actual);
            Assert.That(actual, Is.EqualTo(input), "Bypass to Graphics builder.Output should preserve all bytes exactly.");
        }
        finally
        {
            graph.Destroy();
            if (!output.IsDestroyed) output.Destroy();
            if (!src.IsDestroyed) src.Destroy();
        }
    }

    private static byte[] BuildStaircaseTestImage(int width, int height)
    {
        byte[] data = new byte[width * height * 4];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                int o = (y * width + x) * 4;
                // Preserve varying non-1 alpha to verify alpha pass-through.
                byte alpha = (byte)(140 + ((x + y) % 80));
                data[o + 0] = 20;
                data[o + 1] = 20;
                data[o + 2] = 20;
                data[o + 3] = alpha;

                // Shallow 1:6 white staircase crossing tile boundary x=16 / y=16.
                if (y <= 22 && y >= 15 + (x / 6))
                {
                    data[o + 0] = 240;
                    data[o + 1] = 240;
                    data[o + 2] = 240;
                }

                // Pure blue (0, 0, 255) vs black staircase in lower center (x in [36..64], y in [38..50]).
                if (x >= 36 && x < 64 && y >= 38 && y < 50)
                {
                    int blueStep = 41 + ((x - 36) / 4);
                    bool isBlue = y >= blueStep;
                    data[o + 0] = 0;
                    data[o + 1] = 0;
                    data[o + 2] = isBlue ? (byte)255 : (byte)0;
                }
            }
        }
        return data;
    }

    private static void BuildDepthSceneTest(int width, int height, out byte[] color, out float[] depth)
    {
        color = new byte[width * height * 4];
        depth = new float[width * height];

        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                int idx = y * width + x;
                int o = idx * 4;

                if (x < width / 2)
                {
                    // Left half: smooth sloped planar depth + 1px alternating stripes (must stay untouched under DepthOnly).
                    depth[idx] = 0.85f - 0.004f * x - 0.002f * y;
                    byte stripe = (x % 2 == 0) ? (byte)30 : (byte)220;
                    color[o + 0] = stripe;
                    color[o + 1] = stripe;
                    color[o + 2] = stripe;
                    color[o + 3] = 255;
                }
                else
                {
                    // Right half: foreground geometry (d=0.25) over background (d=0.75) with low luma contrast (52 vs 66)
                    // rejected by high luma threshold (0.333) but rescued by depth edge detection.
                    int stepY = 26 + ((x - width / 2) / 4);
                    bool fg = y >= stepY;
                    depth[idx] = fg ? 0.25f : 0.75f;
                    byte val = fg ? (byte)66 : (byte)52;
                    color[o + 0] = val;
                    color[o + 1] = val;
                    color[o + 2] = val;
                    color[o + 3] = 255;
                }
            }
        }
    }

    private static void AssertComputeAndGraphicsMatch(byte[] computeOut, byte[] graphicsOut, int width, int height, int maxChannelDiff)
    {
        int maxDiff = 0;
        for (int i = 0; i < computeOut.Length; ++i)
        {
            int diff = Math.Abs(computeOut[i] - graphicsOut[i]);
            if (diff > maxDiff) maxDiff = diff;
            Assert.That(diff, Is.LessThanOrEqualTo(maxChannelDiff),
                $"Compute vs Graphics mismatch at pixel {(i / 4) % width},{(i / 4) / width} channel {i % 4}: compute={computeOut[i]}, graphics={graphicsOut[i]}");
        }
        TestContext.WriteLine($"Compute vs Graphics ({width}x{height}): max channel diff = {maxDiff}");
    }

    private static void AssertAlphaPreserved(byte[] input, byte[] actual, int width, int height, string label)
    {
        for (int i = 3; i < input.Length; i += 4)
        {
            Assert.That(actual[i], Is.EqualTo(input[i]),
                $"{label}: Alpha mismatch at pixel {(i / 4) % width},{(i / 4) / width}");
        }
    }

    private static void AssertPixelEquals(byte[] expected, byte[] actual, int width, int x, int y, string label)
    {
        int o = (y * width + x) * 4;
        for (int c = 0; c < 4; ++c)
        {
            Assert.That(actual[o + c], Is.EqualTo(expected[o + c]),
                $"{label}: pixel ({x},{y}) channel {c} changed ({expected[o + c]} -> {actual[o + c]}).");
        }
    }

    private static void AssertRegionBitExact(byte[] expected, byte[] actual, int width, int x0, int y0, int x1, int y1, string label)
    {
        for (int y = y0; y <= y1; ++y)
        {
            for (int x = x0; x <= x1; ++x)
            {
                AssertPixelEquals(expected, actual, width, x, y, label);
            }
        }
    }

    private static void AssertRegionSmoothed(byte[] input, byte[] actual, int width, int x0, int y0, int x1, int y1, int channel, string label)
    {
        int changedIntermediate = 0;
        for (int y = y0; y <= y1; ++y)
        {
            for (int x = x0; x <= x1; ++x)
            {
                int o = (y * width + x) * 4 + channel;
                if (actual[o] != input[o])
                {
                    changedIntermediate++;
                }
            }
        }

        Assert.That(changedIntermediate, Is.GreaterThan(4),
            $"{label}: expected anti-aliased intermediate pixels in [{x0}..{x1}, {y0}..{y1}], got {changedIntermediate}.");
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

    private static string LoadEmbeddedShader(string logicalName)
    {
        Assembly assembly = typeof(RGNode_FXAA).Assembly;
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
