using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Alco.Graphics.AlcoGpu;
using Alco.Graphics.AlcoGpu.Interop;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// End-to-end tests for the alco-gpu backend: device creation, WGSL rendering with
/// pixel validation, buffer readback, async texture readbacks, idempotent managed
/// ownership and queue concurrency.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
public sealed class AlcoGpuIntegrationTests
{
    private sealed class Host : IGPUDeviceHost, IDisposable
    {
        /// <inheritdoc />
        public event Action? OnEndFrame;
        /// <inheritdoc />
        public event Action? OnDispose;

        /// <summary>Runs deferred resource disposal and readback processing.</summary>
        public void EndFrame() => OnEndFrame?.Invoke();
        /// <inheritdoc />
        public void Dispose() => OnDispose?.Invoke();
        /// <inheritdoc />
        public void LogInfo(ReadOnlySpan<char> message) { }
        /// <inheritdoc />
        public void LogWarning(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());
        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }

    private static AlcoGpuDevice CreateDevice(Host host, GraphicsBackend backend = GraphicsBackend.Auto, bool debug = false)
    {
        return new AlcoGpuDevice(new DeviceDescriptor(host, backend, debug: debug));
    }

    private const string FullscreenTriangleWgsl = """
        @vertex
        fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32>
        {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-1.0, -1.0),
                vec2<f32>( 3.0, -1.0),
                vec2<f32>(-1.0,  3.0));
            return vec4<f32>(positions[index], 0.0, 1.0);
        }

        @fragment
        fn fs_main() -> @location(0) vec4<f32>
        {
            // 64/255, 128/255, 191/255: exact in the unorm8 quantization grid.
            return vec4<f32>(0.25, 0.50196078431372548, 0.75, 1.0);
        }
        """;

    /// <summary>Renders a fullscreen WGSL triangle and validates the read-back pixels.</summary>
    /// <param name="backend">The requested graphics backend.</param>
    /// <param name="debug">Whether to enable native debugging and validation.</param>
    [TestCase(GraphicsBackend.Auto, false)]
    [TestCase(GraphicsBackend.WGPUDx12, false)]
    [TestCase(GraphicsBackend.WGPUDx12, true)]
    public unsafe void RenderQuadAndReadbackMatchesExpectedPixels(GraphicsBackend backend, bool debug)
    {
        if (backend == GraphicsBackend.WGPUDx12 && !OperatingSystem.IsWindows())
        {
            Assert.Ignore("Direct3D 12 requires Windows.");
        }
        const uint size = 64;
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host, backend, debug);

        byte[] code = Encoding.UTF8.GetBytes(FullscreenTriangleWgsl);
        var vertexModule = new ShaderModule(ShaderStage.Vertex, ShaderLanguage.WGSL, code, "vs_main");
        var fragmentModule = new ShaderModule(ShaderStage.Fragment, ShaderLanguage.WGSL, code, "fs_main");

        using GPUPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
            Array.Empty<GPUBindGroup>(),
            new[] { vertexModule, fragmentModule },
            Array.Empty<VertexInputLayout>(),
            RasterizerState.CullNone,
            BlendState.Opaque,
            DepthStencilState.None,
            PrimitiveTopology.TriangleList,
            new[] { PixelFormat.RGBA8Unorm },
            null));

        var layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            new[] { new ColorAttachment { Format = PixelFormat.RGBA8Unorm, ClearColor = new(0, 0, 0, 1) } },
            null,
            "readback_layout"));
        using var frameBuffer = device.CreateFrameBuffer(new FrameBufferDescriptor(layout, size, size, "readback_fb"));

        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        commands.Begin();
        using (var renderPass = commands.BeginRender(frameBuffer))
        {
            renderPass.SetPipeline(pipeline);
            renderPass.Draw(3, 1, 0, 0);
        }
        commands.End();
        device.Submit(commands);

        byte[] pixels = new byte[size * size * 4];
        fixed (byte* pointer = pixels)
        {
            device.ReadTexture(frameBuffer.Colors[0], pointer, (uint)pixels.Length);
        }

        // Every pixel is covered by the oversized triangle: (0.25, 0.5, 0.75, 1.0) → (64, 128, 191, 255).
        Assert.That(pixels[0], Is.EqualTo(64));
        Assert.That(pixels[1], Is.EqualTo(128));
        Assert.That(pixels[2], Is.EqualTo(191));
        Assert.That(pixels[3], Is.EqualTo(255));
        Assert.That(pixels[^4], Is.EqualTo(64));
        Assert.That(pixels[^1], Is.EqualTo(255));

        layout.Destroy();
    }

    /// <summary>Round-trips buffer data through queue writes and readbacks.</summary>
    [Test]
    public unsafe void BufferWriteAndReadbackRoundTrips()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);

        using GPUBuffer buffer = device.CreateBuffer(new BufferDescriptor(256,
            BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc));

        byte[] written = new byte[256];
        new Random(1234).NextBytes(written);
        fixed (byte* pointer = written)
        {
            device.WriteBuffer(buffer, 0, pointer, 256);
        }

        byte[] readback = new byte[256];
        fixed (byte* pointer = readback)
        {
            device.ReadBuffer(buffer, pointer, 0, 256);
        }

        Assert.That(readback, Is.EqualTo(written));
    }

    /// <summary>Empty public buffer writes leave existing data intact and the device usable.</summary>
    /// <param name="backend">The requested graphics backend.</param>
    [TestCase(GraphicsBackend.Auto)]
    [TestCase(GraphicsBackend.WGPUDx12)]
    public unsafe void EmptyBufferWritesPreserveContents(GraphicsBackend backend)
    {
        if (backend == GraphicsBackend.WGPUDx12 && !OperatingSystem.IsWindows())
        {
            Assert.Ignore("Direct3D 12 requires Windows.");
        }

        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host, backend);
        using GPUBuffer buffer = device.CreateBuffer(new BufferDescriptor(64,
            BufferUsage.CopyDst | BufferUsage.CopySrc));
        byte[] written = new byte[64];
        new Random(1234).NextBytes(written);
        device.WriteBuffer(buffer, written);

        device.WriteBuffer(buffer, Array.Empty<byte>());
        device.WriteBuffer(buffer, 4, Array.Empty<uint>());
        device.WriteBuffer(buffer, 0, null, 0);

        byte[] readback = new byte[written.Length];
        fixed (byte* pointer = readback)
        {
            device.ReadBuffer(buffer, pointer, 0, (uint)readback.Length);
        }
        Assert.That(readback, Is.EqualTo(written));
    }

    /// <summary>Completes an async texture readback through per-frame polling.</summary>
    [Test]
    public unsafe void AsyncTextureReadbackCompletes()
    {
        const uint size = 32;
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);

        GPUTexture texture = device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, size, size, usage: TextureUsage.Standard));

        byte[] written = new byte[size * size * 4];
        Array.Fill(written, (byte)0x5a);
        fixed (byte* pointer = written)
        {
            device.WriteTexture(texture, pointer, (uint)written.Length);
        }

        byte[] readback = new byte[written.Length];
        var request = new GPUTextureReadbackRequest();

        fixed (byte* pointer = readback)
        {
            device.BeginReadTexture(texture, pointer, (uint)readback.Length, request);

            // Poll at a frame-like cadence: a cold GPU can take over 200 ms of wall
            // time before it executes even this small copy (hybrid-GPU wake-up), so a
            // tight loop would spin through its budget before the fence advances.
            for (int frame = 0; frame < 100 && request.IsPending; frame++)
            {
                device.ProcessPendingReadbacks();
                Thread.Sleep(15);
            }
        }

        Assert.That(request.IsPending, Is.False, "async readback did not complete in 100 polls");
        request.ThrowIfFailed();
        Assert.That(readback, Is.EqualTo(written));

        texture.Destroy();
    }

    /// <summary>Repeated managed disposal releases one native pointer and leaves replacement resources usable.</summary>
    [Test]
    public void RepeatedManagedBufferDisposalIsIdempotentAndKeepsDeviceUsable()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        GPUBuffer buffer = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopyDst | BufferUsage.CopySrc));
        Assert.That(((AlcoGpuBuffer)buffer).Native.IsNull, Is.False);

        Assert.DoesNotThrow(buffer.Dispose);
        Assert.DoesNotThrow(buffer.Dispose);
        host.EndFrame();
        host.EndFrame();
        Assert.DoesNotThrow(() => buffer.Destroy());
        Assert.DoesNotThrow(buffer.Dispose);
        Assert.That(buffer.IsDisposed, Is.True);
        Assert.That(((AlcoGpuBuffer)buffer).Native.IsNull, Is.True);

        // Managed ownership, not repeated raw native destroy, provides idempotence.
        using GPUBuffer replacement = device.CreateBuffer(new BufferDescriptor(64, BufferUsage.CopyDst | BufferUsage.CopySrc));
        byte[] written = new byte[64];
        new Random(4321).NextBytes(written);
        device.WriteBuffer(replacement, written);
        byte[] readback = new byte[written.Length];
        device.ReadBuffer(replacement, readback);
        Assert.That(readback, Is.EqualTo(written));
    }

    /// <summary>The QueryResolve usage bit survives the ABI conversion (bit 9 regression).</summary>
    [Test]
    public unsafe void QueryResolveUsageFlagIsPreserved()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        if (!device.SupportedFeatures.HasFlag(GPUFeatures.TimestampQuery))
        {
            Assert.Ignore("adapter has no timestamp query support");
        }

        AlcoGPU.BufferDesc desc = new()
        {
            Size = 256,
            Usage = (BufferUsage.QueryResolve | BufferUsage.CopyDst | BufferUsage.CopySrc),
        };

        AlcoGpuNative.BufferCreate(device.Native, in desc, out AlcoGPU.BufferHandle buffer);
        AlcoGpuNative.BufferDestroy(buffer);
    }

    /// <summary>Exercises texture streaming concurrently with native queue submissions.</summary>
    /// <param name="updateBuffers">Whether to interleave independent buffer writes with submissions.</param>
    [TestCase(false)]
    [TestCase(true)]
    public unsafe void StreamingUploadsAndSubmissionsCompleteWithCorrectPixels(bool updateBuffers)
    {
        const int iterations = 128;
        const uint size = 512;
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        var descriptor = new TextureDescriptor(TextureDimension.Texture2D, PixelFormat.RGBA8Unorm,
            size, size, usage: TextureUsage.Standard);
        GPUTexture[] sourcesA = new GPUTexture[iterations];
        GPUTexture[] sourcesB = new GPUTexture[iterations];
        GPUCommandBuffer[] submissions = new GPUCommandBuffer[iterations];
        using GPUTexture destination = device.CreateTexture(descriptor);
        using GPUCommandBuffer commands = device.CreateCommandBuffer();
        using GPUBuffer buffer = device.CreateBuffer(new BufferDescriptor(256,
            BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc));
        byte[] bufferData = new byte[256];
        Array.Fill(bufferData, (byte)0x37);
        using var start = new Barrier(3);
        byte[] pixelsA = new byte[size * size * 4];
        byte[] pixelsB = new byte[pixelsA.Length];
        byte[] readback = new byte[pixelsA.Length];
        Array.Fill(pixelsA, (byte)0x5a);
        Array.Fill(pixelsB, (byte)0xb6);

        for (int i = 0; i < iterations; i++)
        {
            sourcesA[i] = device.CreateTexture(descriptor);
            sourcesB[i] = device.CreateTexture(descriptor);
            GPUCommandBuffer submission = device.CreateCommandBuffer();
            submissions[i] = submission;
            submission.Begin();
            submission.CopyTexture(sourcesA[i], destination);
            submission.CopyTexture(sourcesB[i], destination);
            submission.End();
        }
        try
        {
            Task uploadA = Task.Run(() => UploadRepeatedly(device, sourcesA, pixelsA, start));
            Task uploadB = Task.Run(() => UploadRepeatedly(device, sourcesB, pixelsB, start));
            for (int i = 0; i < iterations; i++)
            {
                start.SignalAndWait();
                if (updateBuffers)
                {
                    for (int write = 0; write < 16; write++)
                    {
                        fixed (byte* p = bufferData)
                        {
                            device.WriteBuffer(buffer, 0, p, 256);
                        }
                    }
                }
                device.Submit(submissions[i]);
                start.SignalAndWait();
                if (i % 16 == 0)
                {
                    fixed (byte* pointer = readback)
                    {
                        device.ReadTexture(destination, pointer, (uint)readback.Length);
                    }
                    host.EndFrame();
                }
            }
            Task.WaitAll(uploadA, uploadB);

            if (updateBuffers)
            {
                byte[] bufferReadback = new byte[bufferData.Length];
                fixed (byte* p = bufferReadback)
                {
                    device.ReadBuffer(buffer, p, 0, 256);
                }
                Assert.That(bufferReadback, Is.EqualTo(bufferData));
            }

            foreach (var (source, expected) in new[] { (sourcesA[^1], pixelsA), (sourcesB[^1], pixelsB) })
            {
                commands.Begin();
                commands.CopyTexture(source, destination);
                commands.End();
                device.Submit(commands);
                fixed (byte* pointer = readback)
                {
                    device.ReadTexture(destination, pointer, (uint)readback.Length);
                }
                Assert.That(readback, Is.EqualTo(expected));
            }
        }
        finally
        {
            for (int i = 0; i < iterations; i++)
            {
                submissions[i].Dispose();
                sourcesA[i].Dispose();
                sourcesB[i].Dispose();
            }
        }

    }

    /// <summary>Records independent render and compute commands while uploading buffers concurrently.</summary>
    /// <param name="independentDevices">Whether each recording/upload pair uses its own device.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void ConcurrentRenderComputeRecordingAndBufferUploadsProduceExpectedResults(bool independentDevices)
    {
        RunConcurrentRecordingAndUploads(independentDevices, useBundles: false);
    }

    /// <summary>
    /// Records and executes independent render bundles alongside buffer uploads and partial first-use texture writes.
    /// </summary>
    /// <param name="independentDevices">Whether each recording/upload pair uses its own device.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void ConcurrentBundleRecordingAndPartialFirstUseUploadsProduceExpectedPixels(bool independentDevices)
    {
        RunConcurrentRecordingAndUploads(independentDevices, useBundles: true);
    }

    /// <summary>
    /// Creates and destroys independent resources concurrently on one device and validates worker-local buffer copies.
    /// </summary>
    [Test]
    public void ConcurrentResourceCreationAndDestructionPreservesBufferCopies()
    {
        const int workerCount = 2;
        const int iterations = 32;
        const uint bufferSize = 256;
        var ownedResources = new List<BaseGPUObject>[workerCount];
        var outputs = new GPUBuffer[workerCount];
        var expected = new byte[workerCount][];
        var workers = new Action<Barrier, CancellationToken>[workerCount];
        var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);

        for (int worker = 0; worker < workerCount; worker++)
        {
            int index = worker;
            ownedResources[index] = new List<BaseGPUObject>();
            expected[index] = new byte[bufferSize];
            for (int element = 0; element < expected[index].Length; element++)
            {
                expected[index][element] = (byte)(index * 37 + element);
            }
            workers[index] = (start, cancellation) =>
            {
                T Own<T>(T resource) where T : BaseGPUObject
                {
                    // Each list is written by its worker only and cleaned up after all workers exit.
                    ownedResources[index].Add(resource);
                    return resource;
                }

                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    WaitForConcurrentRound(start, cancellation);
                    GPUBuffer source = Own(device.CreateBuffer(new BufferDescriptor(bufferSize,
                        BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc)));
                    GPUBuffer destination = Own(device.CreateBuffer(new BufferDescriptor(bufferSize,
                        BufferUsage.CopyDst | BufferUsage.CopySrc)));
                    GPUBindGroup layout = Own(device.CreateBindGroup(new BindGroupDescriptor(
                        new[] { new BindGroupEntry(0, ShaderStage.Compute, BindingType.UniformBuffer) })));
                    GPUCommandBuffer commands = Own(device.CreateCommandBuffer());
                    bool submit = iteration == iterations - 1;
                    if (submit)
                    {
                        device.WriteBuffer(source, expected[index]);
                    }
                    commands.Begin();
                    commands.CopyBuffer(source, destination);
                    commands.End();
                    WaitForConcurrentRound(start, cancellation);

                    if (submit)
                    {
                        // Keep submitted resources alive; managed submission preserves core's upload guard.
                        device.Submit(commands);
                        outputs[index] = destination;
                    }
                    commands.Destroy();
                    layout.Destroy();
                    if (!submit)
                    {
                        // Dispose defers native release, so use Destroy to exercise wrapper reclamation here.
                        destination.Destroy();
                        source.Destroy();
                    }
                }
            };
        }

        RunConcurrentWorkers(workers, () =>
        {
            // Readbacks use shared staging state and are deliberately serialized after the join.
            for (int worker = 0; worker < workerCount; worker++)
            {
                byte[] readback = new byte[bufferSize];
                device.ReadBuffer(outputs[worker], readback);
                Assert.That(readback, Is.EqualTo(expected[worker]), $"Worker {worker}, buffer copy");
            }
        }, () =>
        {
            try
            {
                for (int worker = 0; worker < workerCount; worker++)
                {
                    for (int resource = ownedResources[worker].Count - 1; resource >= 0; resource--)
                    {
                        ownedResources[worker][resource].Destroy();
                    }
                }
            }
            finally
            {
                // The helper also defers this cleanup on timeout until native-blocked workers exit.
                host.Dispose();
            }
        });
    }

    private static unsafe void RunConcurrentRecordingAndUploads(bool independentDevices, bool useBundles)
    {
        const int recordingWorkers = 2;
        const int iterations = 32;
        const int bufferElements = 64;
        const uint size = 64;
        const uint regionOffset = 16;
        const uint regionSize = 32;
        const uint bytesPerRow = 256;
        const string computeWgsl = """
            @group(0) @binding(0) var<storage, read_write> values: array<u32>;

            @compute @workgroup_size(64)
            fn cs_main(@builtin(global_invocation_id) id: vec3<u32>)
            {
                values[id.x] = values[id.x] + 3u;
            }
            """;

        var ownedResources = new List<IDisposable>();
        T Own<T>(T resource) where T : IDisposable
        {
            ownedResources.Add(resource);
            return resource;
        }
        void Cleanup()
        {
            List<Exception>? failures = null;
            for (int i = ownedResources.Count - 1; i >= 0; i--)
            {
                try
                {
                    ownedResources[i].Dispose();
                }
                catch (Exception error)
                {
                    (failures ??= new()).Add(error);
                }
            }
            if (failures != null)
            {
                throw new AggregateException(failures);
            }
        }

        var devices = new AlcoGpuDevice[recordingWorkers];
        var pipelines = new GPUPipeline[recordingWorkers];
        var layouts = new GPUAttachmentLayout[recordingWorkers];
        var computeLayouts = new GPUBindGroup?[recordingWorkers];
        var computePipelines = new GPUPipeline?[recordingWorkers];
        var computeOutputs = new GPUBuffer?[recordingWorkers];
        var computeResources = new GPUResourceGroup?[recordingWorkers];
        var frameBuffers = new GPUFrameBuffer[recordingWorkers];
        var commands = new GPUCommandBuffer[recordingWorkers];
        var bundles = new GPURenderBundle?[recordingWorkers];
        var uploadBuffers = new GPUBuffer[recordingWorkers];
        var uploadData = new byte[recordingWorkers][];
        var streamedTextures = new GPUTexture[recordingWorkers][];
        var copyDestinations = new GPUTexture?[recordingWorkers];
        var regionData = new byte[recordingWorkers][];
        bool cleanupTransferred = false;
        try
        {
            // Creation is deliberately single-threaded. Only immutable pipelines/layouts are
            // shared; each worker exclusively owns its encoder, passes, bundle and framebuffer.
            byte[] code = Encoding.UTF8.GetBytes(FullscreenTriangleWgsl);
            for (int worker = 0; worker < recordingWorkers; worker++)
            {
                if (worker != 0 && !independentDevices)
                {
                    devices[worker] = devices[0];
                    pipelines[worker] = pipelines[0];
                    layouts[worker] = layouts[0];
                    computeLayouts[worker] = computeLayouts[0];
                    computePipelines[worker] = computePipelines[0];
                }
                else
                {
                    devices[worker] = CreateDevice(Own(new Host()));
                    pipelines[worker] = Own(devices[worker].CreateGraphicsPipeline(new GraphicsPipelineDescriptor(
                        Array.Empty<GPUBindGroup>(),
                        new[]
                        {
                            new ShaderModule(ShaderStage.Vertex, ShaderLanguage.WGSL, code, "vs_main"),
                            new ShaderModule(ShaderStage.Fragment, ShaderLanguage.WGSL, code, "fs_main"),
                        },
                        Array.Empty<VertexInputLayout>(), RasterizerState.CullNone, BlendState.Opaque,
                        DepthStencilState.None, PrimitiveTopology.TriangleList, new[] { PixelFormat.RGBA8Unorm }, null)));
                    layouts[worker] = Own(devices[worker].CreateAttachmentLayout(new AttachmentLayoutDescriptor(
                        new[] { new ColorAttachment { Format = PixelFormat.RGBA8Unorm, ClearColor = new(0, 0, 0, 1) } },
                        null)));
                    if (!useBundles)
                    {
                        computeLayouts[worker] = Own(devices[worker].CreateBindGroup(new BindGroupDescriptor(
                            new[] { new BindGroupEntry(0, ShaderStage.Compute, BindingType.StorageBuffer) })));
                        computePipelines[worker] = Own(devices[worker].CreateComputePipeline(new ComputePipelineDescriptor(
                            new ShaderModule(ShaderStage.Compute, ShaderLanguage.WGSL,
                                Encoding.UTF8.GetBytes(computeWgsl), "cs_main"),
                            new[] { computeLayouts[worker]! })));
                    }
                }

                AlcoGpuDevice device = devices[worker];
                frameBuffers[worker] = Own(device.CreateFrameBuffer(new FrameBufferDescriptor(layouts[worker], size, size)));
                commands[worker] = Own(device.CreateCommandBuffer());
                uploadBuffers[worker] = Own(device.CreateBuffer(new BufferDescriptor(bufferElements * sizeof(uint),
                    BufferUsage.CopyDst | BufferUsage.CopySrc)));
                uploadData[worker] = new byte[bufferElements * sizeof(uint)];
                Array.Fill(uploadData[worker], (byte)(0x37 + worker * 0x21));
                if (useBundles)
                {
                    bundles[worker] = Own(device.CreateRenderBundle());
                    var descriptor = new TextureDescriptor(TextureDimension.Texture2D, PixelFormat.RGBA8Unorm,
                        size, size, usage: TextureUsage.Standard);
                    streamedTextures[worker] = new GPUTexture[iterations];
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        streamedTextures[worker][iteration] = Own(device.CreateTexture(descriptor));
                    }
                    copyDestinations[worker] = Own(device.CreateTexture(descriptor));
                    regionData[worker] = new byte[bytesPerRow * regionSize];
                    Array.Fill(regionData[worker], (byte)(0x5a + worker * 0x23));
                }
                else
                {
                    computeOutputs[worker] = Own(device.CreateBuffer(new BufferDescriptor(bufferElements * sizeof(uint),
                        BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.CopySrc)));
                    computeResources[worker] = Own(device.CreateResourceGroup(new ResourceGroupDescriptor(
                        computeLayouts[worker]!, new[] { new ResourceBindingEntry(0, computeOutputs[worker]!) })));
                    var initial = new uint[bufferElements];
                    for (int element = 0; element < initial.Length; element++)
                    {
                        initial[element] = (uint)(worker * 1000 + element);
                    }
                    device.WriteBuffer(computeOutputs[worker]!, initial);
                }
            }

            var workers = new Action<Barrier, CancellationToken>[recordingWorkers * 2];
            for (int worker = 0; worker < recordingWorkers; worker++)
            {
                int index = worker;
                workers[worker] = (start, cancellation) =>
                {
                    GPUCommandBuffer recording = commands[index];
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        WaitForConcurrentRound(start, cancellation);
                        if (useBundles)
                        {
                            GPURenderBundle bundle = bundles[index]!;
                            bundle.Begin(layouts[index]);
                            bundle.SetGraphicsPipeline(pipelines[index]);
                            for (int draw = 0; draw < 8; draw++)
                            {
                                bundle.Draw(3, 1, 0, 0);
                            }
                            bundle.End();
                        }
                        recording.Begin();
                        if (!useBundles)
                        {
                            using var computePass = recording.BeginCompute();
                            computePass.SetPipeline(computePipelines[index]!);
                            computePass.SetResources(0, computeResources[index]!);
                            computePass.DispatchCompute(1, 1, 1);
                        }
                        using (var renderPass = recording.BeginRender(frameBuffers[index]))
                        {
                            if (useBundles)
                            {
                                renderPass.ExecuteBundle(bundles[index]!);
                            }
                            else
                            {
                                renderPass.SetPipeline(pipelines[index]);
                                for (int draw = 0; draw < 8; draw++)
                                {
                                    renderPass.Draw(3, 1, 0, 0);
                                }
                            }
                        }
                        if (useBundles)
                        {
                            recording.CopyTexture(streamedTextures[index][iteration], copyDestinations[index]!);
                        }
                        recording.End();
                        // Managed submission retains the texture-upload guard required by core 30.
                        devices[index].Submit(recording);
                        WaitForConcurrentRound(start, cancellation);
                    }
                };
                workers[recordingWorkers + worker] = (start, cancellation) =>
                {
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        WaitForConcurrentRound(start, cancellation);
                        for (int write = 0; write < 16; write++)
                        {
                            devices[index].WriteBuffer(uploadBuffers[index], uploadData[index]);
                        }
                        if (useBundles)
                        {
                            // Each texture is untouched until this partial first-use write. Use the
                            // managed guard rather than the raw ABI to avoid core's lock inversion.
                            fixed (byte* pointer = regionData[index])
                            {
                                devices[index].WriteTextureRegion(streamedTextures[index][iteration], pointer,
                                    (uint)regionData[index].Length, bytesPerRow,
                                    regionOffset, regionOffset, regionSize, regionSize);
                            }
                        }
                        WaitForConcurrentRound(start, cancellation);
                    }
                };
            }

            cleanupTransferred = true;
            RunConcurrentWorkers(workers, () =>
            {
                // The managed staging cache assumes broader ownership than command recording:
                // all readbacks happen on the test thread only after every worker has exited.
                byte[] expectedPixels = new byte[size * size * 4];
                for (int pixel = 0; pixel < expectedPixels.Length; pixel += 4)
                {
                    expectedPixels[pixel] = 64;
                    expectedPixels[pixel + 1] = 128;
                    expectedPixels[pixel + 2] = 191;
                    expectedPixels[pixel + 3] = 255;
                }
                for (int worker = 0; worker < recordingWorkers; worker++)
                {
                    byte[] pixels = new byte[expectedPixels.Length];
                    fixed (byte* pointer = pixels)
                    {
                        devices[worker].ReadTexture(frameBuffers[worker].Colors[0], pointer, (uint)pixels.Length);
                    }
                    Assert.That(pixels, Is.EqualTo(expectedPixels), $"Worker {worker}, rendered pixels");
                    byte[] bufferReadback = new byte[uploadData[worker].Length];
                    devices[worker].ReadBuffer(uploadBuffers[worker], bufferReadback);
                    Assert.That(bufferReadback, Is.EqualTo(uploadData[worker]), $"Worker {worker}, buffer upload");
                    if (!useBundles)
                    {
                        uint[] computed = new uint[bufferElements];
                        devices[worker].ReadBuffer(computeOutputs[worker]!, computed);
                        for (int element = 0; element < computed.Length; element++)
                        {
                            Assert.That(computed[element], Is.EqualTo((uint)(worker * 1000 + element + iterations * 3)),
                                $"Worker {worker}, compute element {element}");
                        }
                    }
                    else
                    {
                        byte[] expected = new byte[pixels.Length];
                        for (uint y = regionOffset; y < regionOffset + regionSize; y++)
                        {
                            expected.AsSpan((int)((y * size + regionOffset) * 4), (int)(regionSize * 4))
                                .Fill(regionData[worker][0]);
                        }
                        // Concurrent submission may precede a given upload. Copy again after all
                        // workers finish to check deterministic pixels and untouched zeroed texels.
                        for (int sample = 0; sample < 2; sample++)
                        {
                            commands[worker].Begin();
                            commands[worker].CopyTexture(streamedTextures[worker][sample == 0 ? 0 : iterations - 1],
                                copyDestinations[worker]!);
                            commands[worker].End();
                            devices[worker].Submit(commands[worker]);
                            fixed (byte* pointer = pixels)
                            {
                                devices[worker].ReadTexture(copyDestinations[worker]!, pointer, (uint)pixels.Length);
                            }
                            Assert.That(pixels, Is.EqualTo(expected), $"Worker {worker}, partial first-use sample {sample}");
                        }
                    }
                }
            }, Cleanup);
        }
        finally
        {
            if (!cleanupTransferred)
            {
                Cleanup();
            }
        }
    }

    private static void WaitForConcurrentRound(Barrier start, CancellationToken cancellation)
    {
        if (!start.SignalAndWait(TimeSpan.FromSeconds(10), cancellation))
        {
            throw new TimeoutException("Concurrent GPU workers did not reach the barrier within 10 seconds.");
        }
    }

    private static void RunConcurrentWorkers(Action<Barrier, CancellationToken>[] workers, Action validate, Action cleanup)
    {
        var start = new Barrier(workers.Length);
        var cancellation = new CancellationTokenSource();
        var tasks = new List<Task>(workers.Length);
        void CleanupAfterWorkers()
        {
            try
            {
                cleanup();
            }
            finally
            {
                start.Dispose();
                cancellation.Dispose();
            }
        }
        try
        {
            for (int i = 0; i < workers.Length; i++)
            {
                Action<Barrier, CancellationToken> worker = workers[i];
                tasks.Add(Task.Factory.StartNew(() =>
                {
                    try
                    {
                        worker(start, cancellation.Token);
                    }
                    catch
                    {
                        cancellation.Cancel();
                        throw;
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
            Task completion = Task.WhenAll(tasks);
            if (Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(60))).GetAwaiter().GetResult() != completion)
            {
                throw new TimeoutException("Concurrent GPU workers did not exit within 60 seconds.");
            }
            completion.GetAwaiter().GetResult();
            validate();
        }
        finally
        {
            cancellation.Cancel();
            Task completion = Task.WhenAll(tasks);
            if (completion.IsCompleted)
            {
                CleanupAfterWorkers();
            }
            else
            {
                // A native-blocked worker cannot be aborted safely. Never destroy its resources
                // or device, or synchronously wait on cleanup, while reporting a timeout/failure.
                _ = completion.ContinueWith(finished =>
                {
                    _ = finished.Exception;
                    try
                    {
                        CleanupAfterWorkers();
                    }
                    catch (Exception error)
                    {
                        Console.WriteLine($"Deferred concurrent GPU test cleanup failed: {error}");
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }
    }

    /// <summary>Creates empty binding layouts and resource groups through the managed API.</summary>
    [Test]
    public void EmptyManagedResourceGroupsCanBeCreated()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);
        using GPUBindGroup layout = device.CreateBindGroup(new BindGroupDescriptor([]));
        using GPUResourceGroup resources = device.CreateResourceGroup(new ResourceGroupDescriptor(layout, []));
    }

    /// <summary>Reconfigures an acquired swapchain before presentation and renders subsequent frames.</summary>
    /// <param name="backend">The requested Windows graphics backend.</param>
    [TestCase(GraphicsBackend.WGPUVulkan)]
    [TestCase(GraphicsBackend.WGPUDx12)]
    [Platform("Win")]
    public void SwapchainResizeAndVSyncRecoverUnpresentedAcquisitions(GraphicsBackend backend)
    {
        using var window = new HiddenWindow();
        using var host = new Host();
        AlcoGpuDevice device;
        try
        {
            device = CreateDevice(host, backend);
        }
        catch (GraphicsException error) when (backend != GraphicsBackend.Auto)
        {
            // An explicitly requested backend may have no driver on this
            // machine (e.g. CI Windows runners without Vulkan).
            throw new IgnoreException($"The {backend} backend is unavailable on this machine ({error.Message}).");
        }
        using GPUSwapchain swapchain = device.CreateSwapchain(new SwapchainDescriptor(
            SurfaceSource.CreateWin32Window(window.Handle, window.Instance), device.PreferredSurfaceFormat,
            PixelFormat.Depth32Float, 64, 64, true));
        using GPUCommandBuffer commands = device.CreateCommandBuffer();

        for (int iteration = 0; iteration < 24; iteration++)
        {
            Acquire(swapchain);
            uint width = (uint)(80 + iteration % 3 * 16);
            uint height = (uint)(80 + iteration % 2 * 16);
            window.Resize(width, height);
            swapchain.Resize(width, height);
            bool vsync = iteration % 2 != 0;
            swapchain.IsVSyncEnabled = vsync;
            Assert.That(swapchain.IsVSyncEnabled, Is.EqualTo(vsync));
            Acquire(swapchain);
            Assert.That(swapchain.FrameBuffer.Width, Is.EqualTo(width));
            Assert.That(swapchain.FrameBuffer.Height, Is.EqualTo(height));
            TextureUsage expectedUsage = TextureUsage.ColorAttachment |
                (backend == GraphicsBackend.WGPUDx12 ? TextureUsage.Read : TextureUsage.TextureBinding);
            Assert.That(swapchain.FrameBuffer.Colors[0].Usage, Is.EqualTo(expectedUsage));

            commands.Begin();
            using (commands.BeginRender(swapchain.FrameBuffer)) { }
            commands.End();
            device.Submit(commands);
            swapchain.Present();
            host.EndFrame();
        }
        swapchain.Destroy();
        host.EndFrame();
        host.EndFrame();
    }

    private static void Acquire(GPUSwapchain swapchain)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (swapchain.RequestSurfaceTexture())
            {
                return;
            }
            Thread.Sleep(10);
        }
        Assert.Fail("The swapchain did not acquire a usable surface texture.");
    }

    private sealed class HiddenWindow : IDisposable
    {
        /// <summary>Gets the application module that owns the native window.</summary>
        public IntPtr Instance { get; } = GetModuleHandleW(null);
        /// <summary>Gets the hidden native window handle.</summary>
        public IntPtr Handle { get; }

        /// <summary>Creates a hidden window for surface lifecycle verification.</summary>
        public HiddenWindow()
        {
            Handle = CreateWindowExW(0, "STATIC", "alco-gpu regression", 0x00CF0000,
                0, 0, 64, 64, IntPtr.Zero, IntPtr.Zero, Instance, IntPtr.Zero);
            Assert.That(Handle, Is.Not.EqualTo(IntPtr.Zero), $"CreateWindowExW failed: {Marshal.GetLastWin32Error()}");
        }

        /// <summary>Changes the native window dimensions without activating it.</summary>
        public void Resize(uint width, uint height)
        {
            Assert.That(SetWindowPos(Handle, IntPtr.Zero, 0, 0, (int)width, (int)height, 0x0014),
                Is.True, $"SetWindowPos failed: {Marshal.GetLastWin32Error()}");
        }

        /// <inheritdoc />
        public void Dispose() => DestroyWindow(Handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? moduleName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowExW(uint extendedStyle, string className, string title,
            uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr window);
    }

    private static unsafe void UploadRepeatedly(GPUDevice device, GPUTexture[] textures,
        byte[] pixels, Barrier start)
    {
        fixed (byte* pointer = pixels)
        {
            for (int i = 0; i < textures.Length; i++)
            {
                start.SignalAndWait();
                device.WriteTexture(textures[i], pointer, (uint)pixels.Length);
                start.SignalAndWait();
            }
        }
    }
}
