using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>Unit tests for high-level <see cref="Alco.Graphics"/> abstractions, Plan A lifecycle, <see cref="RootField{T}"/>, and auto-paging frame arena.</summary>
[TestFixture]
public sealed class HighLevelApiTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PassData
    {
        public Vector4 Tint;
        public float Scale;
        public uint Flags;
        public Vector2 Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DirectComputeRoot
    {
        public GPUBufferView Output;
        public Vector4 Value;
    }

    private const string GraphicsPassShaderSource = """
        import Core;

        struct PassData
        {
            float4 Tint;
            float Scale;
            uint Flags;
            float2 Padding;
        };

        [GPURoot]
        struct HighLevelDrawRoot
        {
            GPUTextureId Albedo;
            GPUSamplerId Sampler;
            GPUBufferView Vertices;
            PassData Pass;
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
            HighLevelDrawRoot root = gpu_load_root<HighLevelDrawRoot>();
            float4 tex = gpu_texture<Texture2D>(root.Albedo.index).Sample(gpu_sampler(root.Sampler.index), input.Uv);
            return tex * root.Pass.Tint * root.Pass.Scale;
        }
        """;

    private const string ComputePassShaderSource = """
        import Core;

        [GPURoot]
        struct DirectComputeRoot
        {
            GPUBufferView Output;
            float4 Value;
        };

        [shader("compute")]
        [numthreads(1, 1, 1)]
        void csMain(uint3 id : SV_DispatchThreadID)
        {
            DirectComputeRoot root = gpu_load_root<DirectComputeRoot>();
            gpu_store(root.Output, id.x, root.Value);
        }
        """;

    private sealed class TrackedDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private static (GPUDevice Device, RenderingSystem System) CreateNoGpuSystem(
        HeapPoolDesc? bufferPool = null,
        HeapPoolDesc? texturePool = null)
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU });
        try
        {
            RenderingSystem system = bufferPool.HasValue || texturePool.HasValue
                ? new RenderingSystem(
                    device,
                    bufferPool ?? new HeapPoolDesc { InitialHeapSize = 1024 * 1024 },
                    texturePool ?? new HeapPoolDesc { InitialHeapSize = 1024 * 1024 })
                : new RenderingSystem(device, textureHeapByteCount: 1024 * 1024, bufferHeapByteCount: 1024 * 1024);
            return (device, system);
        }
        catch
        {
            device.Destroy();
            throw;
        }
    }

    /// <summary>Verifies <see cref="GPUTextureId"/> and <see cref="GPUSamplerId"/> factory, convenience properties, and disposal invalidation.</summary>
    [Test]
    public void BindlessHandles_SampledStorageAndDisposedBehavior_WorkAsExpected()
    {
        (GPUDevice device, RenderingSystem system) = CreateNoGpuSystem();
        try
        {
            Assert.That(GPUTextureId.Invalid.IsValid, Is.False);
            Assert.That(GPUTextureId.Invalid.Index, Is.EqualTo(Texture.NoIndex));
            Assert.That(GPUTextureId.FromSampled(null), Is.EqualTo(GPUTextureId.Invalid));
            Assert.That(GPUTextureId.FromSampled(null, Format.Rgba8Unorm), Is.EqualTo(GPUTextureId.Invalid));
            Assert.That(GPUTextureId.FromStorage(null), Is.EqualTo(GPUTextureId.Invalid));

            Assert.That(GPUSamplerId.Invalid.IsValid, Is.False);
            Assert.That(GPUSamplerId.Invalid.Index, Is.EqualTo(Texture.NoIndex));
            Assert.That(GPUSamplerId.From(null), Is.EqualTo(GPUSamplerId.Invalid));

            using Texture tex = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(8, 8, 1),
                MipLevels = 2,
                Format = Format.Rgba8Srgb,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
                MutableFormat = true,
            });
            using Sampler sampler = system.CreateSampler(new SamplerDesc());

            GPUTextureId sampled = GPUTextureId.FromSampled(tex);
            GPUTextureId unormView = GPUTextureId.FromSampled(tex, Format.Rgba8Unorm);
            GPUTextureId storage0 = GPUTextureId.FromStorage(tex, 0);
            GPUTextureId storage1 = GPUTextureId.FromStorage(tex, 1);
            GPUSamplerId samplerId = GPUSamplerId.From(sampler);

            Assert.That(sampled.IsValid, Is.True);
            Assert.That(sampled, Is.EqualTo(tex.SampledId));
            Assert.That(unormView.IsValid, Is.True);
            Assert.That(unormView, Is.EqualTo(tex.GetSampledId(Format.Rgba8Unorm)));
            Assert.That(unormView, Is.Not.EqualTo(sampled));
            Assert.That(storage0.IsValid, Is.True);
            Assert.That(storage0, Is.EqualTo(tex.StorageId));
            Assert.That(storage1.IsValid, Is.True);
            Assert.That(storage1, Is.EqualTo(tex.GetStorageId(1)));
            Assert.That(storage1, Is.Not.EqualTo(storage0));
            Assert.That(GPUTextureId.FromStorage(tex, 99), Is.EqualTo(GPUTextureId.Invalid));

            Assert.That(samplerId.IsValid, Is.True);
            Assert.That(samplerId, Is.EqualTo(sampler.Id));

            tex.Dispose();
            sampler.Dispose();

            Assert.That(GPUTextureId.FromSampled(tex), Is.EqualTo(GPUTextureId.Invalid));
            Assert.That(GPUTextureId.FromSampled(tex, Format.Rgba8Unorm), Is.EqualTo(GPUTextureId.Invalid));
            Assert.That(GPUTextureId.FromStorage(tex, 0), Is.EqualTo(GPUTextureId.Invalid));
            Assert.That(GPUSamplerId.From(sampler), Is.EqualTo(GPUSamplerId.Invalid));
        }
        finally
        {
            system.Dispose();
            device.Destroy();
        }
    }

    /// <summary>Verifies <see cref="BufferSlice"/> creation, slicing, view offset arithmetic, and bounds checks.</summary>
    [Test]
    public void BufferSlice_SlicingViewArithmeticAndBoundsChecks_WorkAsExpected()
    {
        (GPUDevice device, RenderingSystem system) = CreateNoGpuSystem();
        try
        {
            BufferSlice empty = default;
            Assert.That(empty.IsEmpty, Is.True);
            Assert.That(empty.Buffer, Is.Null);
            Assert.That(empty.View, Is.EqualTo(default(GPUBufferView)));
            Assert.That(empty.Slice(0, 0).IsEmpty, Is.True);

            using Buffer buffer = system.CreateBuffer(256);
            BufferSlice full = buffer;
            Assert.That(full.IsEmpty, Is.False);
            Assert.That(full.Buffer, Is.SameAs(buffer));
            Assert.That(full.ByteOffset, Is.EqualTo(0UL));
            Assert.That(full.SizeBytes, Is.EqualTo(256UL));
            Assert.That(full.View, Is.EqualTo(buffer.View));

            BufferSlice sub = full.Slice(64, 128).Slice(16, 64);
            Assert.That(sub.Buffer, Is.SameAs(buffer));
            Assert.That(sub.ByteOffset, Is.EqualTo(80UL));
            Assert.That(sub.SizeBytes, Is.EqualTo(64UL));
            Assert.That(sub.View.Index, Is.EqualTo(buffer.View.Index));
            Assert.That(sub.View.Offset, Is.EqualTo(buffer.View.Offset + 80u));

            BufferSlice zeroLen = buffer.Slice(256, 0);
            Assert.That(zeroLen.IsEmpty, Is.True);
            Assert.That(zeroLen.ByteOffset, Is.EqualTo(256UL));

            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(257));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Slice(200, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => sub.Slice(65));
            Assert.Throws<ArgumentOutOfRangeException>(() => sub.Slice(32, 48));
        }
        finally
        {
            system.Dispose();
            device.Destroy();
        }
    }

    /// <summary>Verifies <see cref="RenderTarget"/> implicit conversions, cached mip/slice views, and <see cref="RenderPassSignature.From"/>.</summary>
    [Test]
    public void RenderTarget_ConversionsViewCachingAndPassSignature_WorkAsExpected()
    {
        (GPUDevice device, RenderingSystem system) = CreateNoGpuSystem();
        try
        {
            RenderTarget nullTarget = (Texture?)null;
            Assert.That(nullTarget.IsValid, Is.False);

            using Texture color = system.CreateTexture(new TextureDesc
            {
                Type = TextureType.TwoDArray,
                Extent = new Extent3D(32, 16, 1),
                MipLevels = 3,
                LayerCount = 2,
                Format = Format.Rgba16Float,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });
            using Texture depth = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(32, 16, 1),
                Format = Format.D32Float,
                Usage = TextureUsage.DepthStencilAttachment,
            });

            RenderTarget implicitTarget = color;
            RenderTarget explicitDefault = RenderTarget.FromTexture(color);
            RenderTarget mip1Slice1 = RenderTarget.FromTexture(color, 1, 1);
            RenderTarget mip1Slice1Cached = RenderTarget.FromTexture(color, 1, 1);

            Assert.That(implicitTarget.IsValid, Is.True);
            Assert.That(implicitTarget, Is.EqualTo(explicitDefault));
            Assert.That(implicitTarget.Extent, Is.EqualTo(new Extent2D(32, 16)));
            Assert.That(implicitTarget.Format, Is.EqualTo(Format.Rgba16Float));
            Assert.That(implicitTarget.SampleCount, Is.EqualTo(1u));
            Assert.That(implicitTarget.Texture, Is.SameAs(color));

            Assert.That(mip1Slice1.IsValid, Is.True);
            Assert.That(mip1Slice1.Extent, Is.EqualTo(new Extent2D(16, 8)));
            Assert.That(mip1Slice1, Is.EqualTo(mip1Slice1Cached));
            Assert.That(mip1Slice1, Is.Not.EqualTo(implicitTarget));

            RenderingDesc desc = new()
            {
                Colors = [new ColorAttachment { Target = mip1Slice1, Load = LoadOp.Clear }],
                Depth = new DepthAttachment { Target = depth, Load = LoadOp.Clear, Store = StoreOp.Discard },
            };
            RenderPassSignature signature = RenderPassSignature.From(in desc);
            Assert.That(signature.ColorCount, Is.EqualTo(1));
            Assert.That(signature.GetColorFormat(0), Is.EqualTo(Format.Rgba16Float));
            Assert.That(signature.DepthFormat, Is.EqualTo(Format.D32Float));
            Assert.That(signature.StencilFormat, Is.EqualTo(Format.Undefined));
            Assert.That(signature.SampleCount, Is.EqualTo(1u));
        }
        finally
        {
            system.Dispose();
            device.Destroy();
        }
    }

    /// <summary>Verifies Plan A GPU-safe <see cref="IDisposable"/> lifecycle, <see cref="RenderingResource.DestroyImmediate"/>, frame-arena disposal guard, <see cref="RenderingSystem.Defer"/>, and auto-Tick retry.</summary>
    [Test]
    public void PlanALifecycle_DeferredDisposeDeferActionAndExhaustionRetry_WorkAsExpected()
    {
        HeapPoolDesc bufferPool = new() { InitialHeapSize = 128, MaxHeapSize = 128, Budget = 128 };
        HeapPoolDesc texturePool = new() { InitialHeapSize = 1024 * 1024 };
        (GPUDevice device, RenderingSystem system) = CreateNoGpuSystem(bufferPool, texturePool);
        try
        {
            // 1. Idle fast-path releases immediately.
            Buffer idleBuf = system.CreateBuffer(128);
            idleBuf.Dispose();
            Assert.That(idleBuf.IsDisposed, Is.True);
            Assert.That(idleBuf.IsReleased, Is.True);
            idleBuf.Dispose(); // idempotent

            // 2. Active recording protects Dispose() until submission/discard + Tick(), whereas DestroyImmediate releases immediately.
            using RenderContext context = new(system, 256);
            context.BeginFrame();
            Buffer activeBuf = system.CreateBuffer(64);
            Buffer immediateBuf = system.CreateBuffer(64);
            immediateBuf.DestroyImmediate();
            Assert.That(immediateBuf.IsDisposed, Is.True);
            Assert.That(immediateBuf.IsReleased, Is.True);

            bool deferRan = false;
            TrackedDisposable tracked = new();
            system.Defer(() => deferRan = true);
            system.DeferDispose(tracked);
            system.DeferDispose(null);

            activeBuf.Dispose();
            Assert.That(activeBuf.IsDisposed, Is.True);
            Assert.That(activeBuf.IsReleased, Is.False);
            Assert.That(deferRan, Is.False);
            Assert.That(tracked.Disposed, Is.False);

            // Auto-Tick cannot reclaim while frame is still recording.
            Assert.Throws<InvalidOperationException>(() => system.CreateBuffer(128));

            context.DiscardFrame();

            // Auto-Tick retry inside CreateBuffer reclaims activeBuf (plus immediateBuf already free = 128B free).
            using Buffer reused = system.CreateBuffer(128);
            Assert.That(activeBuf.IsReleased, Is.True);
            Assert.That(deferRan, Is.True);
            Assert.That(tracked.Disposed, Is.True);
            reused.Dispose();

            // 3. Idle Defer / DeferDispose executes immediately.
            bool idleRan = false;
            TrackedDisposable idleTracked = new();
            system.Defer(() => idleRan = true);
            system.DeferDispose(idleTracked);
            Assert.That(idleRan, Is.True);
            Assert.That(idleTracked.Disposed, Is.True);

            // 4. Mid-submission retirement + Shutdown drain of Defer callbacks.
            context.BeginFrame();
            bool shutdownRan = false;
            system.Defer(() => shutdownRan = true);
            Buffer arenaOwned = context.UploadTransient<Vector4>(Vector4.One).Buffer!;
            Assert.Throws<InvalidOperationException>(() => arenaOwned.Dispose());
            context.Submit();
            context.Wait();
            Assert.That(shutdownRan, Is.False);
            system.Dispose();
            Assert.That(shutdownRan, Is.True);
        }
        finally
        {
            system.Dispose();
            device.Destroy();
        }
    }

    /// <summary>Verifies <see cref="RootField{T}"/> lookup and validation across <see cref="RootLayout"/>, <see cref="Shader"/>, and <see cref="Material"/>.</summary>
    [Test]
    public void RootField_LookupAndValidation_SucceedOrThrowOnMismatch()
    {
        (GPUDevice device, RenderingSystem system) = CreateNoGpuSystem();
        try
        {
            Shader shader = system.Shaders.GetShaderFromSource("hl_root_field", "hl_root_field.slang", GraphicsPassShaderSource);
            Material material = shader.CreateMaterial("hl_mat");

            RootField<GPUTextureId> albedo = shader.Root.GetField<GPUTextureId>("Albedo");
            RootField<GPUSamplerId> sampler = shader.GetRootField<GPUSamplerId>("Sampler");
            RootField<GPUBufferView> vertices = material.GetRootField<GPUBufferView>("Vertices");
            RootField<PassData> pass = material.GetRootField<PassData>("Pass");

            Assert.That(albedo.IsValid, Is.True);
            Assert.That(sampler.IsValid, Is.True);
            Assert.That(vertices.IsValid, Is.True);
            Assert.That(pass.IsValid, Is.True);

            Assert.That(material.TryGetRootField("Pass", out RootField<PassData> tryPass), Is.True);
            Assert.That(tryPass, Is.EqualTo(pass));

            // Error cases: missing field, wrong size, wrong semantic kind.
            Assert.Throws<InvalidOperationException>(() => shader.GetRootField<PassData>("Missing"));
            Assert.Throws<InvalidOperationException>(() => shader.GetRootField<Vector4>("Pass"));
            Assert.Throws<InvalidOperationException>(() => shader.GetRootField<uint>("Albedo"));
            Assert.Throws<InvalidOperationException>(() => shader.GetRootField<uint>("Sampler"));
            Assert.Throws<InvalidOperationException>(() => shader.GetRootField<ulong>("Vertices"));
            Assert.That(shader.Root.TryGetField("Albedo", out RootField<uint> _), Is.False);
        }
        finally
        {
            system.Dispose();
            device.Destroy();
        }
    }

    /// <summary>Verifies <see cref="RenderContext"/> auto-paging frame arena handles overflow, resets cleanly on DiscardFrame/BeginFrame, and stays zero-alloc in steady state.</summary>
    [Test]
    public void FrameArena_OverflowResetAndZeroSteadyStateAllocations_WorkAsExpected()
    {
        (GPUDevice device, RenderingSystem system) = CreateNoGpuSystem();
        try
        {
            // Small 128-byte main page forces overflow onto dynamic pages.
            using RenderContext context = new(system, frameDataByteCount: 128);

            context.BeginFrame();
            BufferSlice small = context.UploadTransient<Vector4>(new Vector4(1, 2, 3, 4));
            Span<Vector4> bigSpan = context.AllocateTransient<Vector4>(32, out BufferSlice bigSlice); // 512 bytes > 128B main page
            bigSpan.Fill(new Vector4(5, 6, 7, 8));
            BufferSlice anotherBig = context.UploadTransient<Vector4>(bigSpan);

            Assert.That(small.Buffer, Is.Not.Null);
            Assert.That(bigSlice.Buffer, Is.Not.SameAs(small.Buffer));
            Assert.That(anotherBig.Buffer, Is.Not.Null);
            Assert.That(small.SizeBytes, Is.EqualTo(16UL));
            Assert.That(bigSlice.SizeBytes, Is.EqualTo(512UL));

            // Overflow page buffers are arena-owned and cannot be disposed directly.
            Assert.Throws<InvalidOperationException>(() => bigSlice.Buffer!.Dispose());

            // Discard resets main + overflow pages for reuse on the same offset.
            context.DiscardFrame();
            context.BeginFrame();
            BufferSlice smallAfterDiscard = context.UploadTransient<Vector4>(new Vector4(9, 10, 11, 12));
            Span<Vector4> bigAfterDiscard = context.AllocateTransient<Vector4>(32, out BufferSlice bigSliceAfterDiscard);
            bigAfterDiscard.Fill(Vector4.One);

            Assert.That(smallAfterDiscard, Is.EqualTo(small));
            Assert.That(bigSliceAfterDiscard, Is.EqualTo(bigSlice));
            context.Submit();
            context.Wait();

            // Warmed steady-state overflow usage (using already-allocated overflow pages) allocates zero managed bytes.
            long allocated = 0;
            bool allNonEmpty = true;
            for (int frame = 0; frame < 10; ++frame)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                context.BeginFrame();
                Span<Vector4> tmp = context.AllocateTransient<Vector4>(16, out BufferSlice slice);
                tmp[0] = new Vector4(frame);
                context.Submit();
                long delta = GC.GetAllocatedBytesForCurrentThread() - before;
                allNonEmpty &= !slice.IsEmpty;
                if (frame >= 4)
                    allocated += delta;
            }
            context.Wait();
            Assert.That(allNonEmpty, Is.True);
            Assert.That(allocated, Is.EqualTo(0L));
        }
        finally
        {
            system.Dispose();
            device.Destroy();
        }
    }

    /// <summary>Verifies high-level recording helpers: <see cref="RenderContext.CopyBuffer(BufferSlice, BufferSlice)"/>, <see cref="RenderContext.RenderScope.Draw{TPass}"/>, and <see cref="RenderContext.ComputeScope.Dispatch{TRoot}"/>.</summary>
    [Test]
    public void HighLevelRecording_CopyBufferTypedRootDispatchAndPassFieldDraw_RecordWithoutErrors()
    {
        (GPUDevice device, RenderingSystem system) = CreateNoGpuSystem();
        try
        {
            Shader gfxShader = system.Shaders.GetShaderFromSource("hl_draw", "hl_draw.slang", GraphicsPassShaderSource);
            Shader cmpShader = system.Shaders.GetShaderFromSource("hl_cmp", "hl_cmp.slang", ComputePassShaderSource);
            Material gfxMat = gfxShader.CreateMaterial("hl_gfx_mat");
            gfxMat.SetTexture("Albedo", system.TextureWhite);
            gfxMat.SetSampler("Sampler", system.Samplers.LinearClamp);
            RootField<PassData> passField = gfxMat.GetRootField<PassData>("Pass");

            using Texture target = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(16, 16, 1),
                Format = Format.Rgba8Unorm,
                Usage = TextureUsage.ColorAttachment | TextureUsage.Sampled,
            });
            using Buffer gpuOutput = system.CreateBuffer(64, MemoryType.GpuOnly);
            using RenderContext context = new(system, frameDataByteCount: 2048);

            context.BeginFrame();
            BufferSlice src = context.UploadTransient<Vector4>([Vector4.UnitX, Vector4.UnitY]);
            context.CopyBuffer(src, gpuOutput.Slice(0, 32));
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Compute, Access.ShaderWrite);

            using (RenderContext.ComputeScope compute = context.BeginCompute())
            {
                DirectComputeRoot root = new() { Output = gpuOutput.View, Value = new Vector4(2, 3, 4, 5) };
                compute.Dispatch(cmpShader, in root, new Extent3D(1, 1, 1));
            }

            context.Barrier(Stage.Compute, Access.ShaderWrite, Stage.Pixel | Stage.ColorOutput, Access.ShaderRead | Access.ColorWrite);
            using (RenderContext.RenderScope render = context.BeginRenderPass(new RenderingDesc
            {
                Colors = [new ColorAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store }],
            }))
            {
                render.SetViewport(new Viewport { Width = 16, Height = 16 });
                render.SetScissor(new Scissor { Width = 16, Height = 16 });
                PassData pass = new() { Tint = Vector4.One, Scale = 1.0f };
                render.Draw(gfxMat, passField, in pass, vertexCount: 3);
            }

            context.Submit();
            system.WaitIdle();
            Assert.That(context.CurrentFrameId, Is.EqualTo(0UL));
        }
        finally
        {
            system.Dispose();
            device.Destroy();
        }
    }
}
