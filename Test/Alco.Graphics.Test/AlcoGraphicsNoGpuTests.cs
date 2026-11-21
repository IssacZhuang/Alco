using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>Tests covering the NoGPU backend, Slang SpirvCompact compilation/reflection, materials, and RenderGraph.</summary>
[TestFixture]
public sealed class AlcoGraphicsNoGpuTests
{
    private const string ComputeAddShaderSource = """
        import Core;

        [GPURoot]
        struct ComputeRoot
        {
            GPUBufferView Output;
            float4 Factor;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void csMain(uint3 id : SV_DispatchThreadID)
        {
            ComputeRoot root = gpu_load_root<ComputeRoot>();
            float4 val = root.Factor * float(id.x + 1);
            gpu_store(root.Output, id.x, val);
        }
        """;

    private const string GraphicsQuadShaderSource = """
        import Core;

        [GPURoot]
        struct DrawRoot
        {
            GpuTextureId Albedo;
            GpuSamplerId Sampler;
            float4 Tint;
        };

        struct VsOut
        {
            float4 Pos : SV_Position;
            float2 Uv : TEXCOORD0;
        };

        [shader("vertex")]
        VsOut vsMain(uint vertexId : SV_VertexID)
        {
            float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
            VsOut o;
            o.Pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
            o.Uv = uv;
            return o;
        }

        [shader("pixel")]
        float4 psMain(VsOut input) : SV_Target
        {
            DrawRoot root = gpu_load_root<DrawRoot>();
            float4 tex = gpu_texture<Texture2D>(root.Albedo.index).Sample(gpu_sampler(root.Sampler.index), input.Uv);
            return tex * root.Tint;
        }
        """;

    private sealed class TestNode : IRenderGraphNode
    {
        private readonly bool _produceOutput;
        private readonly bool _withDeadPass;
        public int AliveExecutions;
        public int DeadExecutions;

        public TestNode(bool produceOutput = true, bool withDeadPass = true)
        {
            _produceOutput = produceOutput;
            _withDeadPass = withDeadPass;
        }

        public void Setup(RenderGraphBuilder builder)
        {
            RGTexture intermediate = builder.CreateTexture(new RGTextureDesc
            {
                Name = "intermediate",
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });

            var writePass = builder.AddPass("WriteIntermediate", 1);
            writePass.Write(intermediate, RGUsage.ColorAttachmentWrite);

            if (_withDeadPass)
            {
                RGTexture dead = builder.CreateTexture(new RGTextureDesc
                {
                    Name = "dead_target",
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.ColorAttachment,
                });
                var deadPass = builder.AddPass("DeadPass", 2);
                deadPass.Write(dead, RGUsage.ColorAttachmentWrite);
            }

            if (_produceOutput && builder.HasOutput)
            {
                var outPass = builder.AddPass("WriteOutput", 3);
                outPass.Read(intermediate, RGUsage.PixelRead);
                outPass.Write(builder.Output, RGUsage.ColorAttachmentWrite);
                outPass.ProducesOutput();
            }
        }

        public void Execute(in RenderGraphContext context, int passId)
        {
            if (passId == 2)
                DeadExecutions++;
            else
                AliveExecutions++;
        }
    }

    /// <summary>Verifies NoGPU device creation, heap allocation, texture sizing, and timeline signaling.</summary>
    [Test]
    public void NoGpuDevice_ResourcesAndTimeline_WorkWithoutHardwareGpu()
    {
        using GPUDeviceScope deviceScope = new(Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU }));
        GPUDevice device = deviceScope.Device;

        Assert.That(device.ActiveBackend, Is.EqualTo(GraphicsBackend.NoGPU));
        Assert.That(device.SupportsTextureFormat(Format.Rgba8Unorm, TextureUsage.Sampled), Is.True);
        Assert.That(device.SupportsTextureFormat(Format.Undefined, TextureUsage.Sampled), Is.False);

        SizeAlign sizeAlign = device.GetTextureSizeAlign(new TextureDesc
        {
            Extent = new Extent3D(64, 64, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.ColorAttachment,
        });
        Assert.That(sizeAlign.Size, Is.GreaterThanOrEqualTo(64UL * 64 * 4));
        Assert.That(sizeAlign.Align, Is.GreaterThan(0UL));

        GPUHeap heap = device.AllocateGPUHeap(1024, MemoryType.CpuVisible);
        try
        {
            Assert.That(heap.Range.CpuSpan.Length, Is.EqualTo(1024));
            Assert.That(heap.Range.View.Index, Is.Not.EqualTo(0u));
            heap.Range.CpuSpan[0] = 0xAB;
            Assert.That(heap.Range.CpuSpan[0], Is.EqualTo(0xAB));
        }
        finally
        {
            heap.Destroy();
        }
    }

    /// <summary>Verifies that both views of a reacquired swapchain image give the same render-pass signature.</summary>
    [Test]
    public void NoGpuSwapchain_FormatChangeKeepsAttachmentSignaturesInSync()
    {
        using GPUDeviceScope deviceScope = new(Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU }));
        GPUDevice device = deviceScope.Device;
        GPUSwapchain swapchain = device.CreateSwapchain(new SwapchainDesc
        {
            Extent = new Extent2D(16, 16),
            Format = Format.Bgra8Unorm,
        });
        GPUCommandPool? pool = null;
        GPURenderView? freshView = null;
        try
        {
            pool = device.CreateCommandPool(0);
            GPUCommandBuffer commands = pool.BeginCommands();
            SwapchainFrame initialFrame = commands.Acquire(swapchain);
            commands.EndCommands();
            Assert.That(initialFrame.RenderView?.Format, Is.EqualTo(Format.Bgra8Unorm));

            Assert.That(swapchain.TrySetFormat(Format.Bgra8Srgb, ColorSpace.Srgb, out Error error), Is.True);
            Assert.That(error, Is.EqualTo(Error.None));
            pool.Reset();
            commands = pool.BeginCommands();
            SwapchainFrame frame = commands.Acquire(swapchain);
            commands.EndCommands();

            Assert.That(swapchain.Format, Is.EqualTo(Format.Bgra8Srgb));
            Assert.That(frame.RenderView?.Format, Is.EqualTo(swapchain.Format));
            Assert.That(frame.Texture, Is.Not.Null);
            freshView = frame.Texture!.CreateRenderView(default);
            Assert.That(freshView.Format, Is.EqualTo(swapchain.Format));

            RenderPassSignature borrowedPass = RenderPassSignature.From(new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = frame.RenderView }],
            });
            RenderPassSignature freshPass = RenderPassSignature.From(new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = freshView }],
            });
            Assert.That(borrowedPass, Is.EqualTo(new RenderPassSignature(Format.Bgra8Srgb)));
            Assert.That(freshPass, Is.EqualTo(borrowedPass));
        }
        finally
        {
            freshView?.Destroy();
            pool?.Destroy();
            swapchain.Destroy();
        }
    }

    /// <summary>Verifies Slang SpirvCompact compilation, root reflection, RenderingSystem, Material, and RenderContext under NoGPU.</summary>
    [Test]
    public void NoGpuRenderingSystem_SlangShaderAndContext_ExecuteCleanly()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
        RenderingSystem system = new(device, textureHeapByteCount: 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
        RenderContext context = new(system);
        try
        {
            Assert.That(ShaderCompiler.TargetForBackend(GraphicsBackend.NoGPU), Is.EqualTo(ShaderTarget.SpirvCompact));

            Shader computeShader = system.Shaders.GetShaderFromSource("test_compute_add", "test_compute_add.slang", ComputeAddShaderSource);
            Assert.That(computeShader.Kind, Is.EqualTo(ShaderKind.Compute));
            Assert.That(computeShader.Root.Find("Output")?.Kind, Is.EqualTo(MaterialFieldKind.Buffer));
            Assert.That(computeShader.Root.Find("Factor")?.Kind, Is.EqualTo(MaterialFieldKind.Data));

            Shader gfxShader = system.Shaders.GetShaderFromSource("test_gfx_quad", "test_gfx_quad.slang", GraphicsQuadShaderSource);
            Assert.That(gfxShader.Kind, Is.EqualTo(ShaderKind.Graphics));
            Assert.That(gfxShader.Root.Find("Albedo")?.Kind, Is.EqualTo(MaterialFieldKind.Texture));
            Assert.That(gfxShader.Root.Find("Sampler")?.Kind, Is.EqualTo(MaterialFieldKind.Sampler));
            Assert.That(gfxShader.Root.Find("Tint")?.Kind, Is.EqualTo(MaterialFieldKind.Data));

            Buffer outputBuffer = system.CreateBuffer(256);
            Texture offscreen = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(16, 16, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });
            Material computeMat = computeShader.CreateMaterial();
            computeMat.SetBuffer("Output", outputBuffer);
            Vector4 factor = new(1, 2, 3, 4);
            computeMat.SetData("Factor", ref factor);

            Material gfxMat = gfxShader.CreateMaterial();
            gfxMat.SetTexture("Albedo", system.TextureWhite);
            gfxMat.SetSampler("Sampler", system.Samplers.LinearClamp);
            Vector4 tint = Vector4.One;
            gfxMat.SetData("Tint", ref tint);

            try
            {
                context.BeginFrame();
                using (var compute = context.BeginCompute())
                {
                    compute.Dispatch(computeMat, new Extent3D(1, 1, 1));
                }
                RenderingDesc passDesc = new()
                {
                    Colors = [new ColorAttachment { RenderView = offscreen.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store }],
                };
                using (var render = context.BeginRenderPass(passDesc))
                {
                    render.Draw(gfxMat, 3);
                }
                TimelinePoint point = context.Submit();
                point.Wait();
                Assert.That(point.Semaphore?.CompletedValue, Is.GreaterThanOrEqualTo(point.Value));
                gfxMat.WriteMask = 0x7;
                gfxMat.WarmUpPipeline([Format.Rgba8Unorm]).GetAwaiter().GetResult();
            }
            finally
            {
                outputBuffer.Destroy();
                offscreen.Destroy();
            }
        }
        finally
        {
            context.Destroy();
            system.Destroy();
            device.Destroy();
        }
    }

    /// <summary>Verifies RenderGraph dead-pass culling and execution under the NoGPU backend.</summary>
    [Test]
    public void NoGpuRenderGraph_CullsDeadPassesAndExecutesAlivePasses()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
        RenderingSystem system = new(device, textureHeapByteCount: 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
        RenderContext context = new(system);
        RenderGraph graph = new(system, new Extent2D(64, 64), "NoGpuTestGraph");
        try
        {
            Texture output = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });

            TestNode node = new();
            graph.Use(node);

            context.BeginFrame();
            graph.Execute(context, RenderGraphTarget.FromTexture(output));
            context.Submit().Wait();

            Assert.That(node.AliveExecutions, Is.EqualTo(2));
            Assert.That(node.DeadExecutions, Is.EqualTo(0));
            Assert.That(graph.Statistics.CulledPassCount, Is.EqualTo(1));
            Assert.That(graph.Statistics.AlivePassCount, Is.EqualTo(2));

            output.Destroy();
        }
        finally
        {
            graph.Destroy();
            context.Destroy();
            system.Destroy();
            device.Destroy();
        }
    }

    private readonly struct GPUDeviceScope(GPUDevice device) : IDisposable
    {
        public GPUDevice Device { get; } = device;
        public void Dispose() => Device.Destroy();
    }
}
