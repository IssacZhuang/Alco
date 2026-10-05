using System.Text;
using System.Threading;
using Alco.Graphics.AlcoGpu;
using Alco.Graphics.AlcoGpu.Interop;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// End-to-end tests for the alco-gpu backend: device creation, WGSL rendering with
/// pixel validation, buffer readback, async texture readbacks, double-destroy error
/// containment and queue concurrency.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
public sealed class AlcoGpuIntegrationTests
{
    private sealed class Host : IGPUDeviceHost, IDisposable
    {
        public event Action? OnEndFrame;
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

    private static AlcoGpuDevice CreateDevice(Host host, GraphicsBackend backend = GraphicsBackend.WGPUVulkan)
    {
        return new AlcoGpuDevice(new DeviceDescriptor(host, backend));
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
    [Test]
    public unsafe void RenderQuadAndReadbackMatchesExpectedPixels()
    {
        const uint size = 64;
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);

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

    /// <summary>
    /// Double-destroy at the ABI level returns InvalidHandle instead of killing the
    /// process — the core reason the alco-gpu layer exists.
    /// </summary>
    [Test]
    public unsafe void DoubleDestroyReturnsInvalidHandleAndKeepsProcessAlive()
    {
        using var host = new Host();
        AlcoGpuDevice device = CreateDevice(host);

        AlcoBufferDesc desc = new()
        {
            Size = 64,
            Usage = (uint)BufferUsage.Uniform,
        };

        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferCreate(device.Native, in desc, out AlcoHandle buffer));
        Assert.That(buffer.IsNull, Is.False);

        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferDestroy(device.Native, buffer));
        uint second = AlcoGpuNative.BufferDestroy(device.Native, buffer);
        Assert.That(second, Is.EqualTo(AlcoGpuAbi.Status.InvalidHandle));

        // The device still works after the contained failure.
        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferCreate(device.Native, in desc, out AlcoHandle replacement));
        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferDestroy(device.Native, replacement));
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

        AlcoBufferDesc desc = new()
        {
            Size = 256,
            Usage = (uint)(BufferUsage.QueryResolve | BufferUsage.CopyDst | BufferUsage.CopySrc),
        };

        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferCreate(device.Native, in desc, out AlcoHandle buffer));
        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferDestroy(device.Native, buffer));
    }

    /// <summary>Exercises texture streaming concurrently with native queue submissions.</summary>
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
