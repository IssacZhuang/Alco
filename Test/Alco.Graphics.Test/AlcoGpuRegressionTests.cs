using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
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
        /// <summary>Gets the number of informational log records delivered to this host.</summary>
        public int InfoLogCount { get; private set; }
        /// <summary>Processes deferred cleanup for several frames.</summary>
        public void Drain()
        {
            for (int i = 0; i < 8; i++)
            {
                OnEndFrame?.Invoke();
            }
        }
        /// <summary>Disposes the device without first draining its frame-driven readbacks.</summary>
        public void Shutdown() => OnDispose?.Invoke();
        /// <inheritdoc />
        public void Dispose()
        {
            Drain();
            Shutdown();
        }
        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) => InfoLogCount++;
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

    /// <summary>Failed construction clears the router root and drops later logs without a replacement device.</summary>
    [Test]
    public void FailedDeviceConstructionDetachesLogRouterWithoutReplacement()
    {
        using var host = new Host();
        long before = AllocationCount();
        Assert.Throws<GraphicsException>(() =>
            new AlcoGpuDevice(new DeviceDescriptor(host, GraphicsBackend.Auto, pushConstantsSize: uint.MaxValue)));
        Assert.That(host.HasSubscribers, Is.False);
        Assert.That(typeof(AlcoGpuLogRouter).GetField("_attached",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null), Is.Null);

        int delivered = host.InfoLogCount;
        Assert.DoesNotThrow(() => AlcoGpuLogRouter.Route(AlcoGpuAbi.LogLevel.Info, "late construction log"));
        Assert.That(host.InfoLogCount, Is.EqualTo(delivered));
        Assert.DoesNotThrow(host.Drain);
        AssertAllocationBalance(before);
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
        DepthAttachmentInfo info = ((AlcoGpuAttachmentLayout)layout).DepthInfo!.Value;
        AlcoGpuAbi.DepthStencilAttachment attachment = *((AlcoGpuFrameBufferBase)frameBuffer).Native.DepthStencil;
        bool hasStencil = PixelFormatUtility.HasStencil(format);
        Assert.Multiple(() =>
        {
            Assert.That(info.HasDepth, Is.True);
            Assert.That(info.HasStencil, Is.EqualTo(hasStencil));
            Assert.That(info.IsDepthReadOnly, Is.EqualTo(readOnly));
            Assert.That(info.IsStencilReadOnly, Is.EqualTo(readOnly || !hasStencil));
            Assert.That(attachment.DepthLoadOp, Is.EqualTo(readOnly ? AlcoGpuAbi.None : 0u));
            Assert.That(attachment.DepthStoreOp, Is.EqualTo(readOnly ? AlcoGpuAbi.None : 0u));
            Assert.That(attachment.StencilLoadOp, Is.EqualTo(readOnly || !hasStencil ? AlcoGpuAbi.None : 0u));
            Assert.That(attachment.StencilStoreOp, Is.EqualTo(readOnly || !hasStencil ? AlcoGpuAbi.None : 0u));
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
        AlcoGpuAbi.DepthStencilAttachment attachment = GetDepthCache(commands);
        Assert.Multiple(() =>
        {
            Assert.That(attachment.StencilLoadOp, Is.EqualTo(AlcoGpuAbi.None));
            Assert.That(attachment.StencilStoreOp, Is.EqualTo(AlcoGpuAbi.None));
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
            AlcoGpuAbi.DepthStencilAttachment attachment = GetDepthCache(commands);
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
        Assert.That(IsHandleNull(commands, "_encoder"), Is.True);
        commands.Begin();
        using (commands.BeginCompute()) { }
        commands.CopyBuffer(source, destination, 0, 0, 4);
        commands.End();
        device.Submit(commands);
        Assert.DoesNotThrow(() => commands.Destroy());
        Assert.DoesNotThrow(commands.Dispose);
    }

    /// <summary>Failed submission consumes the live command buffer even when a recorded core resource was destroyed.</summary>
    [Test]
    public void FailedSubmissionClearsConsumedBufferAndAllowsIndependentRerecording()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUBuffer source = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopySrc | BufferUsage.CopyDst));
        using GPUBuffer destination = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopyDst | BufferUsage.CopySrc));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        commands.CopyBuffer(source, destination);
        commands.End();
        Assert.That(commands.HasBuffer, Is.True);

        // Submit receives only a live command buffer. Core detects its destroyed recorded
        // resource; the ABI never dereferences the already released source wrapper.
        source.Destroy();
        Assert.That(((AlcoGpuBuffer)source).Native.IsNull, Is.True);
        GraphicsException error = Assert.Throws<GraphicsException>(() => device.Submit(commands))!;
        Assert.That(error.Message, Does.Contain("validation"));
        Assert.That(commands.HasBuffer, Is.False);
        Assert.That(IsHandleNull(commands, "_buffer"), Is.True);
        Assert.That(commands.IsRecording, Is.False);

        using GPUBuffer replacement = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopySrc | BufferUsage.CopyDst));
        byte[] expected = new byte[64];
        new Random(9876).NextBytes(expected);
        device.WriteBuffer(replacement, expected);
        commands.Begin();
        commands.CopyBuffer(replacement, destination);
        commands.End();
        device.Submit(commands);
        byte[] readback = new byte[expected.Length];
        device.ReadBuffer(destination, readback);
        Assert.That(readback, Is.EqualTo(expected));
        Assert.DoesNotThrow(() => commands.Destroy());
        Assert.DoesNotThrow(commands.Dispose);
    }

    /// <summary>Render-pass end consumes its pointer before deferred validation fails at encoder finish.</summary>
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
        Assert.That(IsHandleNull(commands, "_renderPass"), Is.True);
        Assert.That(GetRecordingFlag(commands, "_isRecordingRender"), Is.False);
        GraphicsException? error = Assert.Throws<GraphicsException>(commands.End);
        Assert.That(error!.Message, Does.Contain("validation").And.Not.Contain("invalid render pass handle"));
        Assert.That(commands.IsRecording, Is.False);
        Assert.That(IsHandleNull(commands, "_encoder"), Is.True);
        commands.Begin();
        using (commands.BeginRender(frameBuffer)) { }
        commands.End();
        Assert.DoesNotThrow(() => commands.Destroy());
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
        Assert.That(IsHandleNull(bundle, "_bundleEncoder"), Is.True);
        bundle.Begin(colorLayout);
        bundle.SetGraphicsPipeline(pipeline);
        bundle.Draw(3, 1, 0, 0);
        bundle.End();
        Assert.That(bundle.HasBuffer, Is.True);
        Assert.DoesNotThrow(() => bundle.Destroy());
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
        Assert.DoesNotThrow(() => commands.Destroy());
        Assert.That(commands.IsRecording, Is.False);
        Assert.That(IsHandleNull(commands, "_renderPass"), Is.True);
        Assert.That(IsHandleNull(commands, "_encoder"), Is.True);
        AssertAllocationBalance(before);
        Assert.DoesNotThrow(() => commands.Destroy());
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
        Assert.DoesNotThrow(() => valid.Destroy());
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
        Assert.DoesNotThrow(() => valid.Destroy());
        AssertAllocationBalance(before);
    }

    /// <summary>Device shutdown retires both pending texture readbacks and drains every managed staging ticket.</summary>
    [Test]
    public unsafe void DeviceShutdownRetiresMultiplePendingReadbacksAndStagingTickets()
    {
        const uint size = 32;
        using var host = new Host();
        long before = AllocationCount();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUTexture first = device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, size, size, usage: TextureUsage.Standard));
        using GPUTexture second = device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, size, size, usage: TextureUsage.Standard));
        byte[] expectedFirst = new byte[size * size * 4];
        byte[] expectedSecond = new byte[expectedFirst.Length];
        Array.Fill(expectedFirst, (byte)0x43);
        Array.Fill(expectedSecond, (byte)0xa7);
        fixed (byte* pointer = expectedFirst)
        {
            device.WriteTexture(first, pointer, (uint)expectedFirst.Length);
        }
        fixed (byte* pointer = expectedSecond)
        {
            device.WriteTexture(second, pointer, (uint)expectedSecond.Length);
        }

        byte[] readbackFirst = new byte[expectedFirst.Length];
        byte[] readbackSecond = new byte[expectedSecond.Length];
        var requestFirst = new GPUTextureReadbackRequest();
        var requestSecond = new GPUTextureReadbackRequest();
        fixed (byte* firstPointer = readbackFirst)
        fixed (byte* secondPointer = readbackSecond)
        {
            try
            {
                device.BeginReadTexture(first, firstPointer, (uint)readbackFirst.Length, requestFirst);
                device.BeginReadTexture(second, secondPointer, (uint)readbackSecond.Length, requestSecond);
                Assert.That(requestFirst.IsPending, Is.True);
                Assert.That(requestSecond.IsPending, Is.True);
                AssertStagingState(device, pending: 2, idle: 0);
                // Shutdown itself may deliver completed maps before failing any remaining ones.
                Assert.DoesNotThrow(host.Shutdown);
            }
            finally
            {
                // Never unpin destinations while a native copy/map still has a pending request.
                if (device.IsNativeAlive)
                {
                    host.Shutdown();
                }
            }
        }

        GPUTextureReadbackRequest[] requests = [requestFirst, requestSecond];
        byte[][] readbacks = [readbackFirst, readbackSecond];
        byte[][] expected = [expectedFirst, expectedSecond];
        for (int i = 0; i < requests.Length; i++)
        {
            Assert.That(requests[i].IsCompleted, Is.True, $"Readback {i} was stranded during shutdown.");
            if (requests[i].Status == GPUTextureReadbackStatus.Completed)
            {
                Assert.DoesNotThrow(requests[i].ThrowIfFailed);
                Assert.That(readbacks[i], Is.EqualTo(expected[i]));
            }
            else
            {
                Assert.That(requests[i].Status, Is.EqualTo(GPUTextureReadbackStatus.Failed));
                Assert.That(requests[i].Error, Is.TypeOf<ObjectDisposedException>());
            }
        }
        Assert.That(device.IsNativeAlive, Is.False);
        AssertStagingState(device, pending: 0, idle: 0);
        Assert.DoesNotThrow(device.ProcessPendingReadbacks);
        Assert.DoesNotThrow(first.Dispose);
        Assert.DoesNotThrow(second.Dispose);
        Assert.That(((AlcoGpuTexture)first).Native.IsNull, Is.True);
        Assert.That(((AlcoGpuTexture)second).Native.IsNull, Is.True);
        AssertAllocationBalance(before);
    }

    /// <summary>Core validation releases an acquired readback ticket before an independent readback repopulates the cache.</summary>
    [Test]
    public unsafe void FailedReadbackReleasesStagingTicketAndIndependentReadbackRecoversCache()
    {
        const uint size = 32;
        using var host = new Host();
        long before = AllocationCount();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUTexture valid = device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, size, size, usage: TextureUsage.Standard));
        using GPUTexture unreadable = device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, size, size, usage: TextureUsage.TextureBinding));
        byte[] expected = new byte[size * size * 4];
        new Random(2468).NextBytes(expected);
        fixed (byte* pointer = expected)
        {
            device.WriteTexture(valid, pointer, (uint)expected.Length);
        }
        byte[] readback = new byte[expected.Length];
        var request = new GPUTextureReadbackRequest();
        fixed (byte* pointer = readback)
        {
            try
            {
                // Prime an idle ticket, then acquire it in a real core-invalid copy operation.
                device.ReadTexture(valid, pointer, (uint)readback.Length);
                Assert.That(readback, Is.EqualTo(expected));
                AssertStagingState(device, pending: 0, idle: 1);
                nint destination = (nint)pointer;
                GraphicsException error = Assert.Throws<GraphicsException>(() =>
                    device.BeginReadTexture(unreadable, (byte*)destination, (uint)readback.Length, request))!;
                Assert.That(error.Message, Does.Contain("validation"));
                Assert.That(((AlcoGpuTexture)unreadable).Native.IsNull, Is.False,
                    "The failure uses a live texture with incompatible copy usage, not a freed pointer.");
                Assert.That(request.Status, Is.EqualTo(GPUTextureReadbackStatus.Idle));
                AssertStagingState(device, pending: 0, idle: 0);

                Array.Clear(readback);
                device.BeginReadTexture(valid, pointer, (uint)readback.Length, request);
                for (int poll = 0; poll < 100 && request.IsPending; poll++)
                {
                    device.ProcessPendingReadbacks();
                    if (request.IsPending)
                    {
                        Thread.Sleep(15);
                    }
                }
                Assert.That(request.Status, Is.EqualTo(GPUTextureReadbackStatus.Completed));
                Assert.DoesNotThrow(request.ThrowIfFailed);
                Assert.That(readback, Is.EqualTo(expected));
                AssertStagingState(device, pending: 0, idle: 1);
            }
            finally
            {
                // A failing assertion must still retire the request before unpinning its destination.
                if (device.IsNativeAlive)
                {
                    host.Shutdown();
                }
            }
        }
        AssertStagingState(device, pending: 0, idle: 0);
        Assert.DoesNotThrow(valid.Dispose);
        Assert.DoesNotThrow(unreadable.Dispose);
        AssertAllocationBalance(before);
    }

    /// <summary>Late immediate destruction releases open pass, encoder, and finished buffer wrappers after device invalidation.</summary>
    /// <param name="compute">Whether the open command buffer records a compute pass instead of a render pass.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void LateOpenPassDestroyImmediateClearsHandlesAndRecordingState(bool compute)
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null));
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        long before = AllocationCount();
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        if (compute)
        {
            commands.BeginCompute();
        }
        else
        {
            commands.BeginRender(frameBuffer);
        }
        Assert.That(IsHandleNull(commands, compute ? "_computePass" : "_renderPass"), Is.False);
        Assert.That(IsHandleNull(commands, "_encoder"), Is.False);
        Assert.That(commands.IsRecording, Is.True);
        Assert.That(GetRecordingFlag(commands, compute ? "_isRecordingCompute" : "_isRecordingRender"), Is.True);

        using GPUCommandBuffer finished = device.CreateCommandBuffer();
        finished.Begin();
        finished.End();
        Assert.That(finished.HasBuffer, Is.True);
        using GPURenderBundle openBundle = device.CreateRenderBundle();
        openBundle.Begin(layout);
        using GPURenderBundle finishedBundle = device.CreateRenderBundle();
        finishedBundle.Begin(layout);
        finishedBundle.End();
        host.Dispose();
        Assert.That(device.IsNativeAlive, Is.False);
        Assert.That(IsHandleNull(commands, compute ? "_computePass" : "_renderPass"), Is.False,
            "Device invalidation must not bulk-free independently owned child wrappers.");
        Assert.That(finished.HasBuffer, Is.True);
        Assert.That(IsHandleNull(openBundle, "_bundleEncoder"), Is.False);
        Assert.That(finishedBundle.HasBuffer, Is.True);

        Assert.DoesNotThrow(() => device.DestroyImmediate(commands));
        Assert.DoesNotThrow(() => device.DestroyImmediate(finished));
        Assert.DoesNotThrow(openBundle.Dispose);
        Assert.DoesNotThrow(finishedBundle.Dispose);
        Assert.Multiple(() =>
        {
            Assert.That(commands.IsDisposed, Is.True);
            Assert.That(commands.IsRecording, Is.False);
            Assert.That(GetRecordingFlag(commands, "_isRecordingRender"), Is.False);
            Assert.That(GetRecordingFlag(commands, "_isRecordingCompute"), Is.False);
            Assert.That(IsHandleNull(commands, "_renderPass"), Is.True);
            Assert.That(IsHandleNull(commands, "_computePass"), Is.True);
            Assert.That(IsHandleNull(commands, "_encoder"), Is.True);
            Assert.That(IsHandleNull(commands, "_buffer"), Is.True);
            Assert.That(finished.HasBuffer, Is.False);
            Assert.That(IsHandleNull(finished, "_buffer"), Is.True);
            Assert.That(openBundle.IsRecording, Is.False);
            Assert.That(IsHandleNull(openBundle, "_bundleEncoder"), Is.True);
            Assert.That(finishedBundle.HasBuffer, Is.False);
            Assert.That(IsHandleNull(finishedBundle, "_bundle"), Is.True);
        });
        AssertAllocationBalance(before);
        Assert.DoesNotThrow(() => device.DestroyImmediate(commands));
        Assert.DoesNotThrow(commands.Dispose);
    }

    /// <summary>A borrowed owned attachment keeps its framebuffer alive through collection and remains readable.</summary>
    [Test]
    public unsafe void BorrowedOwnedAttachmentRetainsFramebufferAcrossGarbageCollection()
    {
        const uint size = 16;
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null));
        long before = AllocationCount();
        GPUTexture texture = BorrowOwnedColorAttachment(device, layout, out WeakReference parent);
        try
        {
            byte[] expected = new byte[size * size * 4];
            new Random(1357).NextBytes(expected);
            fixed (byte* pointer = expected)
            {
                device.WriteTexture(texture, pointer, (uint)expected.Length);
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.That(parent.IsAlive, Is.True, "The borrowed attachment must retain its owning framebuffer.");
            Assert.That(texture.IsDisposed, Is.False);
            byte[] readback = new byte[expected.Length];
            fixed (byte* pointer = readback)
            {
                device.ReadTexture(texture, pointer, (uint)readback.Length);
            }
            Assert.That(readback, Is.EqualTo(expected));
            GC.KeepAlive(texture);
        }
        finally
        {
            (parent.Target as GPUFrameBuffer)?.Destroy();
            host.Drain();
        }
        Assert.That(texture.IsDisposed, Is.True);
        AssertAllocationBalance(before);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static GPUTexture BorrowOwnedColorAttachment(AlcoGpuDevice device, GPUAttachmentLayout layout,
        out WeakReference parent)
    {
        GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        parent = new WeakReference(frameBuffer, trackResurrection: true);
        return frameBuffer.Colors[0];
    }

    /// <summary>Late disposal clears independently owned resource pointers, including both pipeline kinds and nested attachments.</summary>
    [Test]
    public void DeviceFirstDisposalReleasesEveryOwnedResourceWrapper()
    {
        const string computeWgsl = "@compute @workgroup_size(1) fn cs_main() { }";
        using var host = new Host();
        long before = AllocationCount();
        AlcoGpuDevice device = CreateDevice(host);
        var resources = new List<BaseGPUObject>();
        T Own<T>(T resource) where T : BaseGPUObject
        {
            resources.Add(resource);
            return resource;
        }

        try
        {
            GPUBuffer buffer = Own(device.CreateBuffer(new BufferDescriptor(64,
                BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc)));
            GPUTexture texture = Own(device.CreateTexture(new TextureDescriptor(
                TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, 16, 16)));
            Own(device.CreateTextureView(new TextureViewDescriptor(texture)));
            Own(device.CreateSampler(new SamplerDescriptor(FilterMode.Nearest, FilterMode.Nearest, FilterMode.Nearest,
                AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge)));
            GPUBindGroup bindings = Own(device.CreateBindGroup(new BindGroupDescriptor([
                new BindGroupEntry(0, ShaderStage.Vertex, BindingType.UniformBuffer)])));
            Own(device.CreateResourceGroup(new ResourceGroupDescriptor(bindings, [new ResourceBindingEntry(0, buffer)])));
            Own(device.CreateGraphicsPipeline(PipelineDescriptor(depthOnly: false)));
            Own(device.CreateComputePipeline(new ComputePipelineDescriptor(
                new ShaderModule(ShaderStage.Compute, ShaderLanguage.WGSL, Encoding.UTF8.GetBytes(computeWgsl), "cs_main"), [])));
            if (device.SupportedFeatures.HasFlag(GPUFeatures.TimestampQuery))
            {
                Own(device.CreateTimestampQuerySet(2, "late_queries"));
            }
            GPUAttachmentLayout layout = Own(device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
                [new ColorAttachment(PixelFormat.RGBA8Unorm)], new DepthAttachment(PixelFormat.Depth32Float))));
            GPUFrameBuffer frameBuffer = Own(device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16)));
            GPUTexture color = frameBuffer.Colors[0];
            GPUTextureView colorView = frameBuffer.ColorViews[0];
            GPUTexture depth = frameBuffer.DepthStencil!;
            GPUTextureView depthView = frameBuffer.DepthStencilView!;
            BaseGPUObject[] nestedAttachments = [color, colorView, depth, depthView];

            // Populate the managed staging cache before device teardown; it must also release its wrappers.
            byte[] expected = new byte[64];
            Array.Fill(expected, (byte)0x39);
            device.WriteBuffer(buffer, expected);
            byte[] readback = new byte[64];
            device.ReadBuffer(buffer, readback);
            Assert.That(readback, Is.EqualTo(expected));
            host.Dispose();
            Assert.That(device.IsNativeAlive, Is.False);

            for (int i = resources.Count - 1; i >= 0; i--)
            {
                BaseGPUObject resource = resources[i];
                if (resource is not GPUFrameBuffer && resource is not GPUAttachmentLayout)
                {
                    Assert.That(IsNativeResourceNull(resource), Is.False,
                        $"{resource.GetType().Name} owns its pointer until explicitly released.");
                }
                Assert.DoesNotThrow(resource.Dispose);
                Assert.DoesNotThrow(resource.Dispose);
                Assert.DoesNotThrow(() => device.DestroyImmediate(resource));
                Assert.That(resource.IsDisposed, Is.True);
                if (resource is not GPUFrameBuffer && resource is not GPUAttachmentLayout)
                {
                    Assert.That(IsNativeResourceNull(resource), Is.True, resource.GetType().Name);
                }
            }
            for (int i = 0; i < nestedAttachments.Length; i++)
            {
                Assert.That(IsNativeResourceNull(nestedAttachments[i]), Is.True,
                    "Late framebuffer disposal must release its nested attachment wrappers.");
            }
            Assert.DoesNotThrow(host.Drain);
            AssertAllocationBalance(before);
        }
        finally
        {
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                resources[i].Destroy();
            }
        }
    }

    /// <summary>
    /// Children retain native cleanup ownership after public device invalidation;
    /// late explicit disposal and finalizers release their wrappers without errors.
    /// </summary>
    [Test]
    public void LateDisposalsAfterDeviceDestroyStaySilent()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        GPUBuffer explicitBuffer = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopyDst));
        using GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(PixelFormat.RGBA8Unorm)], null));
        using GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, 16, 16));
        WeakReference[] orphanChildren = CreateOrphanChildren(device);

        // Capture before releasing the open-pass commands so even automatic GC is observed.
        WeakReference[] openPasses;
        StringWriter console = new();
        TextWriter originalOut = Console.Out;
        Console.SetOut(console);
        try
        {
            // Keep both open-pass commands alive until the host destroys the device.
            openPasses = CreateOrphanOpenPassCommands(device, frameBuffer, host);
            Assert.That(device.IsNativeAlive, Is.False);

            Assert.That(((AlcoGpuBuffer)explicitBuffer).Native.IsNull, Is.False,
                "The child owns its wrapper until late disposal releases it.");
            // Late cleanup uses the child's retained context, not the freed public device pointer.
            Assert.DoesNotThrow(explicitBuffer.Dispose);
            Assert.DoesNotThrow(host.Drain);
            Assert.DoesNotThrow(() => device.DestroyImmediate(explicitBuffer));
            Assert.DoesNotThrow(explicitBuffer.Dispose);
            Assert.That(((AlcoGpuBuffer)explicitBuffer).Native.IsNull, Is.True);

            // Finalizers release retained child contexts after public device invalidation.
            // Finalizable views/groups can retain other finalizable children for another GC cycle.
            for (int collection = 0; collection < 8; collection++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            GC.Collect();
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.That(console.ToString(), Does.Not.Contain("Error in GPUObject"));
        for (int i = 0; i < orphanChildren.Length; i++)
        {
            Assert.That(orphanChildren[i].IsAlive, Is.False,
                "The orphan resource wrapper must be collected after its finalizer runs.");
        }
        for (int i = 0; i < openPasses.Length; i++)
        {
            Assert.That(openPasses[i].IsAlive, Is.False,
                "The orphan open-pass command buffer must be collected after its finalizer runs.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateOrphanOpenPassCommands(AlcoGpuDevice device, GPUFrameBuffer frameBuffer, Host host)
    {
        GPUCommandBuffer render = device.CreateCommandBuffer();
        render.Begin();
        render.BeginRender(frameBuffer);
        GPUCommandBuffer compute = device.CreateCommandBuffer();
        compute.Begin();
        compute.BeginCompute();
        Assert.That(IsHandleNull(render, "_renderPass"), Is.False);
        Assert.That(IsHandleNull(compute, "_computePass"), Is.False);
        WeakReference[] references = [new(render, trackResurrection: true), new(compute, trackResurrection: true)];
        host.Dispose();
        GC.KeepAlive(render);
        GC.KeepAlive(compute);
        return references;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateOrphanChildren(AlcoGpuDevice device)
    {
        GPUBuffer buffer = device.CreateBuffer(new BufferDescriptor(128, BufferUsage.Uniform));
        GPUTexture texture = device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, 16, 16));
        GPUTextureView view = device.CreateTextureView(new TextureViewDescriptor(texture));
        GPUSampler sampler = device.CreateSampler(new SamplerDescriptor(
            FilterMode.Nearest, FilterMode.Nearest, FilterMode.Nearest,
            AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge));
        GPUBindGroup layout = device.CreateBindGroup(new BindGroupDescriptor([
            new BindGroupEntry(0, ShaderStage.Vertex, BindingType.UniformBuffer)]));
        GPUResourceGroup group = device.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = layout,
            Resources = [new ResourceBindingEntry(0, buffer)],
        });
        // All locals become garbage once this method returns; nothing is disposed.
        return [new(buffer, trackResurrection: true), new(texture, trackResurrection: true),
            new(view, trackResurrection: true), new(sampler, trackResurrection: true),
            new(layout, trackResurrection: true), new(group, trackResurrection: true)];
    }

    private static AlcoGpuAbi.DepthStencilAttachment GetDepthCache(GPUCommandBuffer commands) =>
        (AlcoGpuAbi.DepthStencilAttachment)typeof(AlcoGpuCommandBuffer).GetField("_depthStencilAttachmentCache",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(commands)!;

    private static bool IsHandleNull(BaseGPUObject instance, string field)
    {
        object handle = instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
        return (bool)handle.GetType().GetProperty("IsNull")!.GetValue(handle)!;
    }

    private static void AssertStagingState(AlcoGpuDevice device, int pending, int idle)
    {
        static object Field(AlcoGpuDevice instance, string name) =>
            typeof(AlcoGpuDevice).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
        var requests = (System.Collections.ICollection)Field(device, "_pendingTextureReadbacks");
        var evicted = (System.Collections.ICollection)Field(device, "_stagingCacheEvicted");
        object cache = Field(device, "_stagingCache");
        Assert.Multiple(() =>
        {
            Assert.That(requests.Count, Is.EqualTo(pending), "Unexpected pending staging ownership.");
            Assert.That(cache.GetType().GetProperty("IdleCount")!.GetValue(cache), Is.EqualTo(idle));
            Assert.That(evicted.Count, Is.Zero, "Evicted tickets must be detached and consumed before returning.");
            if (idle == 0)
            {
                Assert.That(cache.GetType().GetProperty("IdleCapacityBytes")!.GetValue(cache), Is.EqualTo(0UL));
            }
        });
    }

    private static bool IsNativeResourceNull(BaseGPUObject resource)
    {
        object handle = resource.GetType().GetProperty("Native")!.GetValue(resource)!;
        return (bool)handle.GetType().GetProperty("IsNull")!.GetValue(handle)!;
    }

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
