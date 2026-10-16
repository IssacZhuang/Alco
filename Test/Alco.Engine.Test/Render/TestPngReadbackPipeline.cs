#nullable enable

using System.Runtime.InteropServices;
using System.Threading;
using Alco.Graphics;
using Alco.Graphics.AlcoGpu;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// Tests for <see cref="PngReadbackPipeline"/> as a pure transport: the PNG encodes
/// exactly the texture's RGBA — alpha included — so transparent pixels survive to
/// the file untouched. Alpha semantics belong to the producing pass (verified for
/// the blit shaders in <see cref="TestCaptureBlitAlpha"/>); this fixture covers the
/// full pipeline (GPU readback, PNG encode, decode) against the real alco-gpu
/// backend.
/// </summary>
[TestFixture]
public class TestPngReadbackPipeline
{
    /// <summary>
    /// The readback preserves alpha through the full pipeline: a GPU texture with
    /// alpha-0, alpha-128 and alpha-255 bands encodes to a PNG whose alpha channel
    /// matches the source — transparent pixels stay transparent.
    /// </summary>
    [Test]
    [Category("AlcoGpu")]
    [NonParallelizable]
    public unsafe void TestReadbackEncodesTextureAlphaVerbatim()
    {
        byte[] source = CreateBandedPixels();
        byte[] decoded = ReadBackThroughPipeline(source);

        Assert.That(decoded.Length, Is.EqualTo(source.Length));
        for (int i = 0; i < decoded.Length; i += 4)
        {
            Assert.That(decoded[i + 3], Is.EqualTo(source[i + 3]), $"alpha at pixel {i / 4}");
            Assert.That(decoded[i], Is.EqualTo(source[i]), $"red at pixel {i / 4}");
            Assert.That(decoded[i + 1], Is.EqualTo(source[i + 1]), $"green at pixel {i / 4}");
            Assert.That(decoded[i + 2], Is.EqualTo(source[i + 2]), $"blue at pixel {i / 4}");
        }
    }

    /// <summary>
    /// Runs the full pipeline over a source image: uploads it to an RGBA8 GPU texture,
    /// reads it back through a <see cref="PngReadbackPipeline"/>, then decodes the
    /// produced PNG back to RGBA8 pixels.
    /// </summary>
    /// <param name="source">RGBA8 source pixels (width * height * 4 bytes).</param>
    /// <returns>The decoded PNG's RGBA8 pixels.</returns>
    private static unsafe byte[] ReadBackThroughPipeline(byte[] source)
    {
        const int size = TestImageSize;
        using TestDeviceHost host = new();
        AlcoGpuDevice device = new(new DeviceDescriptor(host, GraphicsBackend.Auto, debug: false));
        GPUTexture texture = device.CreateTexture(new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm, size, size, usage: TextureUsage.Standard));
        PngReadbackPipeline pipeline = new(device);
        RenderCaptureResult? result = null;
        try
        {
            fixed (byte* pointer = source)
            {
                device.WriteTexture(texture, pointer, (uint)source.Length);
            }

            Assert.That(pipeline.TryBeginRead(
                texture, captured => result = captured, out RenderCaptureResult? failure), Is.True,
                () => failure?.Error);
            PumpUntilDelivered(device, pipeline, ref result);
        }
        finally
        {
            pipeline.Dispose();
            texture.Destroy();
        }

        host.EndFrame();
        host.EndFrame();

        Assert.That(result, Is.Not.Null, "the readback never delivered a result");
        Assert.That(result!.Success, Is.True, result.Error);
        Assert.That(result.PngBytes, Is.Not.Null);

        fixed (byte* png = result.PngBytes!)
        {
            byte* decoded = ImageDecodeUtility.DecodePng(new ReadOnlySpan<byte>(png, result.PngBytes!.Length), out int width, out int height);
            Assert.That(width, Is.EqualTo(size));
            Assert.That(height, Is.EqualTo(size));
            byte[] pixels = new byte[width * height * 4];
            new Span<byte>(decoded, pixels.Length).CopyTo(pixels);
            NativeMemory.Free(decoded);
            return pixels;
        }
    }

    /// <summary>
    /// Drives the pipeline the way the engine's frame loop does — process pending
    /// GPU readbacks, then pump — until the completion callback delivers a result.
    /// </summary>
    private static void PumpUntilDelivered(GPUDevice device, PngReadbackPipeline pipeline, ref RenderCaptureResult? result)
    {
        for (int frame = 0; frame < 100 && result == null; frame++)
        {
            device.ProcessPendingReadbacks();
            pipeline.Pump();
            if (result == null)
            {
                Thread.Sleep(15);
            }
        }
    }

    /// <summary>The square test image's side in pixels.</summary>
    private const int TestImageSize = 32;

    /// <summary>
    /// Creates the RGBA8 test image: three horizontal bands over a constant RGB with
    /// alpha 0, 128 and 255, so alpha handling is observable independently of RGB.
    /// </summary>
    private static byte[] CreateBandedPixels()
    {
        byte[] alphas = [0, 128, 255];
        byte[] pixels = new byte[TestImageSize * TestImageSize * 4];
        int bandHeight = TestImageSize / alphas.Length;
        for (int y = 0; y < TestImageSize; y++)
        {
            byte alpha = alphas[Math.Min(y / bandHeight, alphas.Length - 1)];
            for (int x = 0; x < TestImageSize; x++)
            {
                int offset = (y * TestImageSize + x) * 4;
                pixels[offset] = 10;
                pixels[offset + 1] = 20;
                pixels[offset + 2] = 30;
                pixels[offset + 3] = alpha;
            }
        }

        return pixels;
    }

    /// <summary>
    /// The minimal <see cref="IGPUDeviceHost"/> a bare device needs: deferred
    /// destroys and readback processing hang off its end-of-frame event.
    /// </summary>
    private sealed class TestDeviceHost : IGPUDeviceHost, IDisposable
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
        public void LogWarning(ReadOnlySpan<char> message) { }

        /// <inheritdoc />
        public void LogError(ReadOnlySpan<char> message) => TestContext.Progress.WriteLine(message.ToString());

        /// <inheritdoc />
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }
}
