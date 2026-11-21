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

    /// <summary>Verifies that NoGPU swapchains resolve Undefined to Bgra8Unorm for Srgb and Rgba16Float for ExtendedSrgbLinear.</summary>
    [Test]
    public void NoGpuSwapchain_UndefinedFormat_ResolvesDefaultPerColorSpace()
    {
        using GPUDeviceScope deviceScope = new(Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU }));
        GPUDevice device = deviceScope.Device;
        GPUSwapchain srgbSwapchain = device.CreateSwapchain(new SwapchainDesc
        {
            Extent = new Extent2D(16, 16),
            Format = Format.Undefined,
            ColorSpace = ColorSpace.Srgb,
        });
        GPUSwapchain hdrSwapchain = device.CreateSwapchain(new SwapchainDesc
        {
            Extent = new Extent2D(16, 16),
            Format = Format.Undefined,
            ColorSpace = ColorSpace.ExtendedSrgbLinear,
        });
        try
        {
            Assert.That(srgbSwapchain.Format, Is.EqualTo(Format.Bgra8Unorm));
            Assert.That(hdrSwapchain.Format, Is.EqualTo(Format.Rgba16Float));

            Assert.That(srgbSwapchain.TrySetFormat(Format.Undefined, ColorSpace.ExtendedSrgbLinear, out Error error), Is.True);
            Assert.That(error, Is.EqualTo(Error.None));
            Assert.That(srgbSwapchain.Format, Is.EqualTo(Format.Rgba16Float));
            Assert.That(srgbSwapchain.ColorSpace, Is.EqualTo(ColorSpace.ExtendedSrgbLinear));

            Assert.That(srgbSwapchain.TrySetFormat(Format.Undefined, ColorSpace.Srgb, out error), Is.True);
            Assert.That(error, Is.EqualTo(Error.None));
            Assert.That(srgbSwapchain.Format, Is.EqualTo(Format.Bgra8Unorm));
            Assert.That(srgbSwapchain.ColorSpace, Is.EqualTo(ColorSpace.Srgb));
        }
        finally
        {
            hdrSwapchain.Destroy();
            srgbSwapchain.Destroy();
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
            Buffer indexBuffer = system.CreateBuffer<ushort>([0, 1, 2]);
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
                Assert.That(outputBuffer.View.Index, Is.Not.EqualTo(0u));
                context.BeginFrame();
                using (var compute = context.BeginCompute())
                {
                    compute.Dispatch(computeMat, new Extent3D(1, 1, 1));
                }
                RenderingDesc passDesc = new()
                {
                    Colors = [new ColorAttachment { RenderView = offscreen.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store }],
                };
                GPUAllocation<byte> packedRoot = gfxMat.AllocateRoot(context.FrameData);
                using (var render = context.BeginRenderPass(passDesc))
                {
                    render.Draw(gfxMat, 3);
                    render.DrawIndexed(gfxMat, packedRoot.GPURange, default, indexBuffer, IndexType.UInt16, 3);
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
                indexBuffer.Destroy();
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

    /// <summary>Verifies RenderContext DiscardFrame/Retain, public Material.CopyRoot, and system-owned UploadTexture/FlushUploads.</summary>
    [Test]
    public void NoGpuContext_DiscardRetainCopyRootAndSystemUploadTexture()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
        RenderingSystem system = new(device, textureHeapByteCount: 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
        RenderContext context = new(system);
        try
        {
            Buffer dummy = system.CreateBuffer(64);
            Texture uploadTarget = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(8, 8, 1),
                Format = Format.R8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
            });
            Shader shader = system.Shaders.GetShaderFromSource("copy_root_test", "copy_root_test.slang", GraphicsQuadShaderSource);
            Material parent = shader.CreateMaterial("parent");
            MaterialInstance child = parent.CreateInstance();
            try
            {
                // System-queued UploadTexture survives a discarded RenderContext frame and retires cleanly.
                Span<byte> texels = stackalloc byte[4 * 4];
                texels.Fill(0x7f);
                system.UploadTexture(uploadTarget, texels, new TextureCopyDesc
                {
                    Offset = new Extent3D(1, 1, 0),
                    Extent = new Extent3D(4, 4, 1),
                    RowPitchBytes = 4,
                });

                context.BeginFrame();
                context.Retain(dummy);
                context.Retain(uploadTarget);
                context.DiscardFrame();
                Assert.That(uploadTarget.IsDestroyed, Is.False);

                TimelinePoint uploadCompletion = system.FlushUploads();
                uploadCompletion.Wait();
                Assert.That(uploadCompletion.Semaphore, Is.Not.Null);

                // Queue another upload and destroy before flushing to verify deferred reclamation at shutdown.
                system.UploadTexture(uploadTarget, texels, new TextureCopyDesc
                {
                    Offset = new Extent3D(2, 2, 0),
                    Extent = new Extent3D(4, 4, 1),
                    RowPitchBytes = 4,
                });
                uploadTarget.Destroy();
                Assert.That(uploadTarget.IsDestroyed, Is.True);

                context.BeginFrame();
                context.Submit().Wait();

                // CopyRoot on Material and MaterialInstance (including padded destination span).
                Vector4 parentTint = new(0.25f, 0.5f, 0.75f, 1.0f);
                Vector4 childTint = new(1.0f, 0.25f, 0.5f, 0.75f);
                parent.SetData("Tint", ref parentTint);
                child.SetData("Tint", ref childTint);

                Span<byte> parentBytes = new byte[(int)parent.Root.SizeBytes + 16];
                Span<byte> childBytes = new byte[(int)child.Root.SizeBytes + 16];
                parent.CopyRoot(parentBytes);
                child.CopyRoot(childBytes);

                uint tintOffset = 0;
                foreach (RootField field in shader.Root.LeafFields)
                {
                    if (field.Name == "Tint")
                        tintOffset = field.Offset;
                }
                Assert.That(MemoryMarshal.Read<Vector4>(parentBytes.Slice((int)tintOffset, 16)), Is.EqualTo(parentTint));
                Assert.That(MemoryMarshal.Read<Vector4>(childBytes.Slice((int)tintOffset, 16)), Is.EqualTo(childTint));
            }
            finally
            {
                shader.Destroy();
                if (!uploadTarget.IsDestroyed)
                    uploadTarget.Destroy();
                dummy.Destroy();
            }
        }
        finally
        {
            context.Destroy();
            system.Destroy();
            device.Destroy();
        }
    }

    /// <summary>Verifies configurable upload staging capacity, double-buffered tiling flushes, and UploadTexture/ReadTexture validation.</summary>
    [Test]
    public void NoGpu_UploadStagingDoubleBufferAndReadTextureValidation()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RenderingSystem(device, textureHeapByteCount: 1024 * 1024, uploadStagingByteCount: 8));

            RenderingSystem defaultSystem = new(device, textureHeapByteCount: 1024 * 1024);
            try
            {
                Assert.That(defaultSystem.UploadStagingByteCount, Is.EqualTo(16ul * 1024 * 1024));
            }
            finally
            {
                defaultSystem.Destroy();
            }

            RenderingSystem system = new(
                device,
                textureHeapByteCount: 1024 * 1024,
                bufferHeapByteCount: 1024 * 1024,
                uploadStagingByteCount: 1025);
            try
            {
                Assert.That(system.UploadStagingByteCount, Is.EqualTo(1040ul));

                Texture noDestination = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(4, 4, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.Sampled,
                });
                Texture uploadOnly = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(4, 4, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.Sampled | TextureUsage.TransferDestination,
                });
                Texture readWrite = system.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(4, 4, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.Sampled | TextureUsage.TransferDestination | TextureUsage.TransferSource,
                });
                try
                {
                    byte[] pixels = new byte[4 * 4 * 4];
                    Assert.Throws<InvalidOperationException>(() => system.UploadTexture(noDestination, pixels));
                    Assert.Throws<InvalidOperationException>(() => system.ReadTexture(uploadOnly, pixels));
                    Assert.Throws<ArgumentException>(() => system.ReadTexture(readWrite, new byte[4]));

                    system.UploadTexture(readWrite, pixels);
                    byte[] readback = new byte[4 * 4 * 4];
                    system.ReadTexture(readWrite, readback);

                    readWrite.Destroy();
                    Assert.Throws<ObjectDisposedException>(() => system.UploadTexture(readWrite, pixels));
                    Assert.Throws<ObjectDisposedException>(() => system.ReadTexture(readWrite, readback));
                }
                finally
                {
                    if (!readWrite.IsDestroyed)
                        readWrite.Destroy();
                    uploadOnly.Destroy();
                    noDestination.Destroy();
                }

                // Double-buffered UploadQueue splits uploads exceeding capacity at half-capacity batch boundaries.
                UploadQueue tiledQueue = new(device, capacity: 64, maxPendingBatches: 2);
                GPUTextureHeap tileHeap = device.CreateTextureHeap(device.GetTextureSizeAlign(new TextureDesc
                {
                    Extent = new Extent3D(4, 8, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.TransferDestination,
                }).Size);
                GPUTexture tileTarget = device.CreateTexture(new TextureDesc
                {
                    Extent = new Extent3D(4, 8, 1),
                    Format = Format.Rgba8Unorm,
                    Usage = TextureUsage.TransferDestination,
                }, tileHeap, 0);
                try
                {
                    tiledQueue.InitializeTexture(tileTarget);
                    TextureUpload.UploadTexture(
                        tiledQueue,
                        tileTarget,
                        new TextureDesc
                        {
                            Extent = new Extent3D(4, 8, 1),
                            Format = Format.Rgba8Unorm,
                            Usage = TextureUsage.TransferDestination,
                        },
                        new byte[4 * 8 * 4]);
                    tiledQueue.Wait();
                    UploadQueueStats stats = tiledQueue.Stats;
                    Assert.That(stats.Submissions, Is.GreaterThanOrEqualTo(2ul));
                    Assert.That(stats.PeakBytes, Is.LessThanOrEqualTo(64ul));
                    Assert.That(stats.BytesInUse, Is.Zero);
                }
                finally
                {
                    tiledQueue.Destroy();
                    tileTarget.Destroy();
                    tileHeap.Destroy();
                }
            }
            finally
            {
                system.Destroy();
            }
        }
        finally
        {
            device.Destroy();
        }
    }

    private readonly struct GPUDeviceScope(GPUDevice device) : IDisposable
    {
        public GPUDevice Device { get; } = device;
        public void Dispose() => Device.Destroy();
    }
}
