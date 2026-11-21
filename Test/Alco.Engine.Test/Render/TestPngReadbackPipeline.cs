#nullable enable

using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>
/// Tests that the PNG readback pipeline preserves texture RGBA values, including alpha, end to end.
/// </summary>
[TestFixture]
public class TestPngReadbackPipeline
{
    private const int TestImageSize = 32;

    /// <summary>
    /// Verifies that GPU readback encodes RGBA8 bands with alpha 0, 128, and 255 verbatim.
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

    private static unsafe byte[] ReadBackThroughPipeline(byte[] source)
    {
        const uint size = TestImageSize;
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.Auto, Headless = true });
        RenderingSystem rendering = new(device, textureHeapByteCount: 4 * 1024 * 1024, bufferHeapByteCount: 1 * 1024 * 1024);
        RenderContext uploadContext = new(rendering);
        Texture texture = rendering.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(size, size, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination,
        });
        PngReadbackPipeline pipeline = new(device);
        RenderCaptureResult? result = null;
        try
        {
            uploadContext.BeginFrame();
            uploadContext.SetTextureData(texture, source);
            uploadContext.Submit().Wait();

            Assert.That(pipeline.TryBeginRead(
                texture, captured => result = captured, out RenderCaptureResult? failure), Is.True,
                () => failure?.Error);
            PumpUntilDelivered(pipeline, ref result);
        }
        finally
        {
            device.WaitIdle();
            pipeline.Dispose();
            texture.Destroy();
            uploadContext.Destroy();
            rendering.Destroy();
            device.Destroy();
        }

        Assert.That(result, Is.Not.Null, "the readback never delivered a result");
        Assert.That(result!.Success, Is.True, result.Error);
        Assert.That(result.PngBytes, Is.Not.Null);

        fixed (byte* png = result.PngBytes!)
        {
            byte* decoded = ImageDecodeUtility.DecodePng(new ReadOnlySpan<byte>(png, result.PngBytes!.Length), out int width, out int height);
            try
            {
                Assert.That(width, Is.EqualTo(TestImageSize));
                Assert.That(height, Is.EqualTo(TestImageSize));
                byte[] pixels = new byte[width * height * 4];
                new Span<byte>(decoded, pixels.Length).CopyTo(pixels);
                return pixels;
            }
            finally
            {
                NativeMemory.Free(decoded);
            }
        }
    }

    private static void PumpUntilDelivered(PngReadbackPipeline pipeline, ref RenderCaptureResult? result)
    {
        for (int frame = 0; frame < 100 && result == null; frame++)
        {
            pipeline.Pump();
            if (result == null)
            {
                Thread.Sleep(15);
            }
        }
    }

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
}
