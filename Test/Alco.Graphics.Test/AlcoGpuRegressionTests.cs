using System.Reflection;
using System.Text;
using Alco.Graphics.AlcoGpu;
using Alco.Graphics.AlcoGpu.Interop;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>Regression coverage for depth aspects, consuming handles, and temporary native allocations.</summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
public sealed class AlcoGpuRegressionTests
{
    private sealed class Host : IGPUDeviceHost, IDisposable
    {
        /// <inheritdoc />
        public event Action? OnEndFrame;
        /// <inheritdoc />
        public event Action? OnDispose;
        /// <summary>Gets whether a device has subscribed any host lifecycle handlers.</summary>
        public bool HasSubscribers => OnEndFrame != null || OnDispose != null;
        /// <summary>Processes deferred cleanup for several frames.</summary>
        public void Drain()
        {
            for (int i = 0; i < 8; i++)
            {
                OnEndFrame?.Invoke();
            }
        }
        /// <inheritdoc />
        public void Dispose()
        {
            Drain();
            OnDispose?.Invoke();
        }
        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) { }
        /// <inheritdoc />
        public void LogWarning(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }

    private const string TriangleWgsl = """
        @vertex
        fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32>
        {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-1.0, -1.0), vec2<f32>(3.0, -1.0), vec2<f32>(-1.0, 3.0));
            return vec4<f32>(positions[index], 0.25, 1.0);
        }
        @fragment
        fn fs_depth() { }
        @fragment
        fn fs_color() -> @location(0) vec4<f32>
        {
            return vec4<f32>(1.0, 0.0, 0.0, 1.0);
        }
        """;

    private static AlcoGpuDevice CreateDevice(Host host) =>
        new(new DeviceDescriptor(host, GraphicsBackend.Auto));

    private static GPUAttachmentLayout CreateDepthLayout(AlcoGpuDevice device, PixelFormat format, bool readOnly = false) =>
        device.CreateAttachmentLayout(new AttachmentLayoutDescriptor([], new DepthAttachment(format)
        {
            ReadOnly = readOnly,
            ClearDepth = 0.75f,
            ClearStencil = 3,
        }));

    private static GraphicsPipelineDescriptor PipelineDescriptor(bool depthOnly, VertexInputLayout[]? vertexLayouts = null)
    {
        byte[] source = Encoding.UTF8.GetBytes(TriangleWgsl);
        return new GraphicsPipelineDescriptor([], [
                new ShaderModule(ShaderStage.Vertex, ShaderLanguage.WGSL, source, "vs_main"),
                new ShaderModule(ShaderStage.Fragment, ShaderLanguage.WGSL, source, depthOnly ? "fs_depth" : "fs_color")],
            vertexLayouts ?? [], RasterizerState.CullNone, BlendState.Opaque,
            depthOnly ? DepthStencilState.Write : DepthStencilState.None,
            PrimitiveTopology.TriangleList, depthOnly ? [] : [PixelFormat.RGBA8Unorm],
            depthOnly ? PixelFormat.Depth32Float : null);
    }

    /// <summary>Failed device construction detaches host handlers and preserves the original native failure.</summary>
    [Test]
    public void FailedDeviceConstructionRollsBackHostSubscriptions()
    {
        using var host = new Host();
        long before = AllocationCount();
        GraphicsException? error = Assert.Throws<GraphicsException>(() =>
            new AlcoGpuDevice(new DeviceDescriptor(host, GraphicsBackend.Auto, pushConstantsSize: uint.MaxValue)));
        Assert.That(error!.Message, Does.Contain("alco-gpu").And.Not.Contain("invalid device handle"));
        Assert.That(host.HasSubscribers, Is.False);
        Assert.DoesNotThrow(host.Drain);
        AssertAllocationBalance(before);
        Assert.DoesNotThrow(() => CreateDevice(host));
        Assert.That(host.HasSubscribers, Is.True);
    }

    /// <summary>Depth-only metadata omits stencil operations independently of layout read-only state.</summary>
    /// <param name="format">The attachment format whose aspects are inspected.</param>
    /// <param name="readOnly">Whether the layout declares its present aspects read-only.</param>
    [TestCase(PixelFormat.Depth16Unorm, false)]
    [TestCase(PixelFormat.Depth24Plus, false)]
    [TestCase(PixelFormat.Depth32Float, false)]
    [TestCase(PixelFormat.Depth32Float, true)]
    [TestCase(PixelFormat.Depth24PlusStencil8, false)]
    [TestCase(PixelFormat.Depth24PlusStencil8, true)]
    public unsafe void AttachmentMetadataSeparatesAspectPresenceFromReadOnly(PixelFormat format, bool readOnly)
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = CreateDepthLayout(device, format, readOnly);
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        AlcoDepthAttachmentInfo info = ((AlcoGpuAttachmentLayout)layout).DepthInfo!.Value;
        AlcoDepthStencilAttachment attachment = *((AlcoGpuFrameBufferBase)frameBuffer).Native.DepthStencil;
        bool hasStencil = PixelFormatUtility.HasStencil(format);
        Assert.Multiple(() =>
        {
            Assert.That(info.HasDepth, Is.True);
            Assert.That(info.HasStencil, Is.EqualTo(hasStencil));
            Assert.That(info.IsDepthReadOnly, Is.EqualTo(readOnly));
            Assert.That(info.IsStencilReadOnly, Is.EqualTo(readOnly || !hasStencil));
            Assert.That(attachment.DepthLoadOp, Is.EqualTo(readOnly ? AlcoGpuAbi.AlcoNone : 0u));
            Assert.That(attachment.DepthStoreOp, Is.EqualTo(readOnly ? AlcoGpuAbi.AlcoNone : 0u));
            Assert.That(attachment.StencilLoadOp, Is.EqualTo(readOnly || !hasStencil ? AlcoGpuAbi.AlcoNone : 0u));
            Assert.That(attachment.StencilStoreOp, Is.EqualTo(readOnly || !hasStencil ? AlcoGpuAbi.AlcoNone : 0u));
        });
    }

    /// <summary>Default, explicit clear, load, and discard depth passes remain valid without stencil.</summary>
    /// <param name="mode">The default, clear, load, discard, or combined operation variant.</param>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void DepthOnlyPassAcceptsClearsAndDepthOps(int mode)
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = CreateDepthLayout(device, PixelFormat.Depth32Float);
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        GPUCommandBuffer.RenderPass pass = mode switch
        {
            0 => commands.BeginRender(frameBuffer),
            1 => commands.BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty, clearDepth: 1, clearStencil: 7),
            2 => commands.BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                depthOps: new AttachmentOps { LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Store }),
            3 => commands.BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                depthOps: new AttachmentOps { LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Discard }),
            _ => commands.BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty, clearDepth: 0.5f, clearStencil: 2,
                depthOps: new AttachmentOps { LoadOp = AttachmentLoadOp.Load, StoreOp = AttachmentStoreOp.Discard }),
        };
        AlcoDepthStencilAttachment attachment = GetDepthCache(commands);
        Assert.Multiple(() =>
        {
            Assert.That(attachment.StencilLoadOp, Is.EqualTo(AlcoGpuAbi.AlcoNone));
            Assert.That(attachment.StencilStoreOp, Is.EqualTo(AlcoGpuAbi.AlcoNone));
            Assert.That(attachment.DepthLoadOp, Is.EqualTo(mode is 1 or 3 or 4 ? 1u : 0u));
            // Explicit clears preserve the existing clear/store precedence.
            Assert.That(attachment.DepthStoreOp, Is.EqualTo(mode == 3 ? 1u : 0u));
        });
        Assert.DoesNotThrow(pass.Dispose);
        Assert.DoesNotThrow(commands.End);
        Assert.That(commands.IsRecording, Is.False);
        Assert.DoesNotThrow(() => device.Submit(commands));
    }

    /// <summary>Stencil-bearing passes apply explicit shared operations to both present channels.</summary>
    [Test]
    public void DepthStencilPassAppliesSharedOpsAndClears()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = CreateDepthLayout(device, PixelFormat.Depth24PlusStencil8);
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        using (commands.BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty,
            depthOps: new AttachmentOps { LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Discard }))
        {
            AlcoDepthStencilAttachment attachment = GetDepthCache(commands);
            Assert.That(attachment.DepthLoadOp, Is.EqualTo(1u));
            Assert.That(attachment.StencilLoadOp, Is.EqualTo(1u));
            Assert.That(attachment.DepthStoreOp, Is.EqualTo(1u));
            Assert.That(attachment.StencilStoreOp, Is.EqualTo(1u));
        }
        using (commands.BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty, clearDepth: 1, clearStencil: 9)) { }
        commands.End();
        device.Submit(commands);
    }

    /// <summary>Present read-only channels reject mutation, while absent stencil clears are harmless.</summary>
    /// <param name="format">The depth-only or depth-stencil format to validate.</param>
    [TestCase(PixelFormat.Depth32Float)]
    [TestCase(PixelFormat.Depth24PlusStencil8)]
    public void ReadOnlyPassRejectsPresentChannelChangesAndRemainsUsable(PixelFormat format)
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = CreateDepthLayout(device, format, readOnly: true);
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        Assert.Throws<InvalidOperationException>(() => commands.BeginRender(frameBuffer,
            ReadOnlySpan<ClearColorData>.Empty, clearDepth: 1));
        Assert.Throws<InvalidOperationException>(() => commands.BeginRender(frameBuffer,
            ReadOnlySpan<ClearColorData>.Empty, depthOps: new AttachmentOps()));
        if (PixelFormatUtility.HasStencil(format))
        {
            Assert.Throws<InvalidOperationException>(() => commands.BeginRender(frameBuffer,
                ReadOnlySpan<ClearColorData>.Empty, clearStencil: 1));
        }
        else
        {
            using (commands.BeginRender(frameBuffer, ReadOnlySpan<ClearColorData>.Empty, clearStencil: 1)) { }
        }
        using (commands.BeginRender(frameBuffer)) { }
        commands.End();
        device.Submit(commands);
    }

    /// <summary>Records real depth-writing draws directly and through a depth-only render bundle.</summary>
    [Test]
    public void DepthOnlyDrawAndBundleAreCompatible()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = CreateDepthLayout(device, PixelFormat.Depth32Float);
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUPipeline pipeline = device.CreateGraphicsPipeline(PipelineDescriptor(depthOnly: true));
        using GPURenderBundle bundle = device.CreateRenderBundle();
        bundle.Begin(layout);
        bundle.SetGraphicsPipeline(pipeline);
        bundle.Draw(3, 1, 0, 0);
        bundle.End();
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        using (GPUCommandBuffer.RenderPass pass = commands.BeginRender(frameBuffer,
            ReadOnlySpan<ClearColorData>.Empty, clearDepth: 1))
        {
            pass.SetPipeline(pipeline);
            pass.Draw(3, 1, 0, 0);
            pass.ExecuteBundle(bundle);
        }
        commands.End();
        device.Submit(commands);
        Assert.That(commands.HasBuffer, Is.False);
    }

    /// <summary>Failed encoder finish clears its consumed handle, permits re-recording, and cleans up.</summary>
    [Test]
    public void InvalidEncodingCanBeCapturedThenRerecordedAndDisposed()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUBuffer source = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopySrc));
        using GPUBuffer destination = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopyDst));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        // Core records copy validation and reports it when the encoder is finished.
        commands.CopyBuffer(source, destination, 1, 0, 4);
        GraphicsException? error = Assert.Throws<GraphicsException>(commands.End);
        Assert.That(error!.Message, Does.Contain("validation").And.Not.Contain("invalid encoder handle"));
        Assert.That(commands.IsRecording, Is.False);
        Assert.That(commands.HasBuffer, Is.False);
        Assert.That(GetHandle(commands, "_encoder").IsNull, Is.True);
        commands.Begin();
        using (commands.BeginCompute()) { }
        commands.CopyBuffer(source, destination, 0, 0, 4);
        commands.End();
        device.Submit(commands);
        Assert.DoesNotThrow(commands.Destroy);
        Assert.DoesNotThrow(commands.Dispose);
    }

    /// <summary>Failed render-pass end consumes its handle and restores public pass state.</summary>
    [Test]
    public void InvalidRenderPassEndRestoresRecordingState()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null));
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUPipeline pipeline = device.CreateGraphicsPipeline(PipelineDescriptor(depthOnly: false));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        GPUCommandBuffer.RenderPass pass = commands.BeginRender(frameBuffer);
        pass.SetPipeline(pipeline);
        pass.SetScissorRect(0, 0, 32, 32);
        pass.Draw(3, 1, 0, 0);
        // Pass end consumes the handle; validation is reported by encoder finish.
        pass.Dispose();
        Assert.That(GetHandle(commands, "_renderPass").IsNull, Is.True);
        Assert.That(GetRecordingFlag(commands, "_isRecordingRender"), Is.False);
        GraphicsException? error = Assert.Throws<GraphicsException>(commands.End);
        Assert.That(error!.Message, Does.Contain("validation").And.Not.Contain("invalid render pass handle"));
        Assert.That(commands.IsRecording, Is.False);
        Assert.That(GetHandle(commands, "_encoder").IsNull, Is.True);
        commands.Begin();
        using (commands.BeginRender(frameBuffer)) { }
        commands.End();
        Assert.DoesNotThrow(commands.Destroy);
    }

    /// <summary>Failed bundle finish preserves validation instead of destroying the consumed encoder again.</summary>
    [Test]
    public void InvalidBundleCanBeCapturedThenRerecordedAndDisposed()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout depthLayout = CreateDepthLayout(device, PixelFormat.Depth32Float);
        using GPUAttachmentLayout colorLayout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null));
        using GPUPipeline pipeline = device.CreateGraphicsPipeline(PipelineDescriptor(depthOnly: false));
        using GPURenderBundle bundle = device.CreateRenderBundle();
        bundle.Begin(depthLayout);
        bundle.SetGraphicsPipeline(pipeline);
        bundle.Draw(3, 1, 0, 0);
        GraphicsException? error = Assert.Throws<GraphicsException>(bundle.End);
        Assert.That(error!.Message, Does.Contain("validation").And.Not.Contain("invalid bundle encoder handle"));
        Assert.That(bundle.IsRecording, Is.False);
        Assert.That(bundle.HasBuffer, Is.False);
        Assert.That(GetHandle(bundle, "_bundleEncoder").IsNull, Is.True);
        bundle.Begin(colorLayout);
        bundle.SetGraphicsPipeline(pipeline);
        bundle.Draw(3, 1, 0, 0);
        bundle.End();
        Assert.That(bundle.HasBuffer, Is.True);
        Assert.DoesNotThrow(bundle.Destroy);
        Assert.DoesNotThrow(bundle.Dispose);
    }

    /// <summary>Disposal frees command caches after deferred pass validation failure without re-recording.</summary>
    [Test]
    public void InvalidPassDisposalPreservesValidationAndReleasesManagedNativeAllocations()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null));
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        using GPUPipeline pipeline = device.CreateGraphicsPipeline(PipelineDescriptor(depthOnly: false));
        long before = AllocationCount();
        GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        GPUCommandBuffer.RenderPass pass = commands.BeginRender(frameBuffer);
        pass.SetPipeline(pipeline);
        pass.SetScissorRect(0, 0, 32, 32);
        pass.Draw(3, 1, 0, 0);
        // End implicitly consumes the still-open pass, then exposes its validation error.
        GraphicsException? error = Assert.Throws<GraphicsException>(commands.End);
        Assert.That(error!.Message, Does.Contain("validation").And.Not.Contain("invalid render pass handle"));
        Assert.DoesNotThrow(commands.Destroy);
        Assert.That(commands.IsRecording, Is.False);
        Assert.That(GetHandle(commands, "_renderPass").IsNull, Is.True);
        Assert.That(GetHandle(commands, "_encoder").IsNull, Is.True);
        AssertAllocationBalance(before);
        Assert.DoesNotThrow(commands.Destroy);
        Assert.DoesNotThrow(commands.Dispose);
    }

    /// <summary>Duplicate layout validation repeatedly frees its temporary native entry array.</summary>
    [Test]
    public void RepeatedDuplicateLayoutsKeepTrackedAllocationsBalanced()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        VerifyAllocationTracking();
        var descriptor = new BindGroupDescriptor([
            new BindGroupEntry(0, ShaderStage.Vertex, BindingType.UniformBuffer),
            new BindGroupEntry(0, ShaderStage.Vertex, BindingType.UniformBuffer)]);
        long before = AllocationCount();
        for (int i = 0; i < 128; i++)
        {
            GraphicsException? error = Assert.Throws<GraphicsException>(() => device.CreateBindGroup(descriptor));
            Assert.That(error!.Message, Does.Contain("validation"));
        }
        AssertAllocationBalance(before);
        GPUBindGroup valid = device.CreateBindGroup(new BindGroupDescriptor([
            new BindGroupEntry(0, ShaderStage.Vertex, BindingType.UniformBuffer)]));
        Assert.DoesNotThrow(valid.Destroy);
        AssertAllocationBalance(before);
    }

    /// <summary>Pipeline validation failures repeatedly free vertex arrays and transient shader modules.</summary>
    [Test]
    public void RepeatedInvalidPipelinesKeepTrackedAllocationsBalanced()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        VerifyAllocationTracking();
        var invalidLayout = new VertexInputLayout([
            new VertexElement(0, 0, VertexFormat.Float32x2, "first"),
            new VertexElement(0, 8, VertexFormat.Float32x2, "duplicate")], 16, VertexStepMode.Vertex);
        GraphicsPipelineDescriptor descriptor = PipelineDescriptor(depthOnly: false, [invalidLayout]);
        long before = AllocationCount();
        for (int i = 0; i < 64; i++)
        {
            GraphicsException? error = Assert.Throws<GraphicsException>(() => device.CreateGraphicsPipeline(descriptor));
            Assert.That(error!.Message, Does.Contain("validation"));
        }
        AssertAllocationBalance(before);
        GPUPipeline valid = device.CreateGraphicsPipeline(PipelineDescriptor(depthOnly: false));
        Assert.DoesNotThrow(valid.Destroy);
        AssertAllocationBalance(before);
    }

    private static AlcoDepthStencilAttachment GetDepthCache(GPUCommandBuffer commands) =>
        (AlcoDepthStencilAttachment)typeof(AlcoGpuCommandBuffer).GetField("_depthStencilAttachmentCache",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;

    private static AlcoHandle GetHandle(BaseGPUObject instance, string field) =>
        (AlcoHandle)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static bool GetRecordingFlag(GPUCommandBuffer commands, string field) =>
        (bool)typeof(GPUCommandBuffer).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;

    private static long AllocationCount()
    {
#if DEBUG
        return InteropUtility.OutstandingAllocationCount;
#else
        return 0;
#endif
    }

    private static unsafe void VerifyAllocationTracking()
    {
#if !DEBUG
        Assert.Ignore("Exact native allocation accounting requires a Debug build.");
#endif
        long before = AllocationCount();
        byte* probe = InteropUtility.Alloc<byte>(17);
        try
        {
            Assert.That(AllocationCount(), Is.EqualTo(before + 1));
        }
        finally
        {
            InteropUtility.Free(probe);
        }
        AssertAllocationBalance(before);
    }

    private static void AssertAllocationBalance(long before)
    {
#if DEBUG
        Assert.That(AllocationCount(), Is.EqualTo(before),
            "Failed creation or disposal must not retain a native temporary array.");
#endif
    }
}
