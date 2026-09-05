using Alco.Graphics.WebGPU;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>Exercises texture streaming concurrently with native queue submissions.</summary>
[TestFixture]
[NonParallelizable]
[Category("WebGPU")]
public sealed class WebGPUQueueConcurrencyTests
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

    /// <summary>
    /// Regresses the wgpu tracker/texture-initialization lock inversion when a
    /// streaming worker writes a texture used by an overlapping submission.
    /// Run with the test runner's hang timeout to bound a native deadlock.
    /// </summary>
    /// <param name="updateBuffers">Also races buffer writes against texture uploads.</param>
    [TestCase(false)]
    [TestCase(true)]
    public unsafe void StreamingUploadsAndSubmissionsCompleteWithCorrectPixels(bool updateBuffers)
    {
        const int iterations = 128;
        const uint size = 512;
        using var host = new Host();
        var device = new WebGPUDevice(new DeviceDescriptor(host, GraphicsBackend.WGPUVulkan));
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

        // Record the first use BEFORE uploading, as streaming textures are visible
        // to rendering while their content is still pending. Reusing initialized
        // textures would omit the submit-time initialization that takes the lock.
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
                        device.WriteBuffer(buffer, bufferData);
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
                device.ReadBuffer(buffer, 0, bufferReadback);
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
