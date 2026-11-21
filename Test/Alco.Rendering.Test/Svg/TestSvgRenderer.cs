using Alco.Graphics;
using Alco.Graphics.LowLevel;
using NUnit.Framework;

namespace Alco.Rendering.Test;

/// <summary>Tests bindless compute <see cref="SvgRenderer"/> rasterization across NoGPU, VulkanCompact, and Dx12.</summary>
[TestFixture]
public unsafe class TestSvgRenderer
{
    private const string SolidRectSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <rect x="16" y="16" width="32" height="32" fill="#00ff00"/>
        </svg>
        """;

    private const string CircleSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <circle cx="32" cy="32" r="20" fill="#ff0000" fill-opacity="0.5"/>
        </svg>
        """;

    private const string GradientSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64">
          <defs>
            <linearGradient id="g" x1="0%" y1="0%" x2="100%" y2="0%">
              <stop offset="0" stop-color="#ff0000"/>
              <stop offset="1" stop-color="#0000ff"/>
            </linearGradient>
          </defs>
          <rect x="0" y="0" width="64" height="64" fill="url(#g)"/>
        </svg>
        """;

    /// <summary>Verifies TryParseAndBuild rejects invalid SVG XML and zero dimensions.</summary>
    [Test]
    public void TryParseAndBuild_ValidatesInputs()
    {
        Assert.That(SvgRenderer.TryParseAndBuild("", 64, 64, out _), Is.False);
        Assert.That(SvgRenderer.TryParseAndBuild("<not-xml", 64, 64, out _), Is.False);
        Assert.That(SvgRenderer.TryParseAndBuild(SolidRectSvg, 0, 64, out _), Is.False);
        Assert.That(SvgRenderer.TryParseAndBuild(SolidRectSvg, 64, 0, out _), Is.False);

        Assert.That(SvgRenderer.TryParseAndBuild(SolidRectSvg, 64, 64, out SvgRasterizeData data), Is.True);
        Assert.That(data.ShapeMeta, Has.Count.EqualTo(1));
    }

    /// <summary>Verifies solid fill, circle AA premultiplication, and linear gradient SVG rasterization.</summary>
    [TestCase(GraphicsBackend.NoGPU)]
    [TestCase(GraphicsBackend.VulkanCompact)]
    [TestCase(GraphicsBackend.Dx12)]
    public void Rasterize_ProducesExpectedPremultipliedPixels(GraphicsBackend backend)
    {
        using RenderingTestHost host = Utility.CreateRenderingSystem(backend);
        using SvgRenderer renderer = new(host.RenderingSystem);
        RenderContext context = new(host.RenderingSystem);
        try
        {
            Assert.That(SvgRenderer.TryParseAndBuild(SolidRectSvg, 64, 64, out SvgRasterizeData rectData), Is.True);
            Assert.That(SvgRenderer.TryParseAndBuild(CircleSvg, 64, 64, out SvgRasterizeData circleData), Is.True);
            Assert.That(SvgRenderer.TryParseAndBuild(GradientSvg, 64, 64, out SvgRasterizeData gradData), Is.True);

            context.BeginFrame();
            using Texture2D rectTex = renderer.Rasterize(context, in rectData, "svg_rect");
            using Texture2D circleTex = renderer.Rasterize(context, in circleData, "svg_circle");
            using Texture2D gradTex = renderer.Rasterize(context, in gradData, "svg_grad");
            context.Submit().Wait();

            if (backend == GraphicsBackend.NoGPU)
                return;

            byte[] rectPixels = ReadbackRgba8(host.Device, context, rectTex.NativeTexture, 64, 64);
            // Outside (2, 2): transparent zero
            AssertPixel(rectPixels, 64, 2, 2, 0, 0, 0, 0);
            // Center (32, 32): opaque green (0, 255, 0, 255)
            AssertPixel(rectPixels, 64, 32, 32, 0, 255, 0, 255);

            byte[] circlePixels = ReadbackRgba8(host.Device, context, circleTex.NativeTexture, 64, 64);
            // Inside center (32, 32): half-alpha red premultiplied (~128, 0, 0, 128)
            int centerIdx = (32 * 64 + 32) * 4;
            Assert.That(circlePixels[centerIdx + 0], Is.InRange((byte)124, (byte)132));
            Assert.That(circlePixels[centerIdx + 1], Is.EqualTo((byte)0));
            Assert.That(circlePixels[centerIdx + 2], Is.EqualTo((byte)0));
            Assert.That(circlePixels[centerIdx + 3], Is.InRange((byte)124, (byte)132));
            // Premultiplied invariant across entire image
            for (int i = 0; i < circlePixels.Length; i += 4)
            {
                Assert.That(circlePixels[i + 0], Is.LessThanOrEqualTo(circlePixels[i + 3]));
                Assert.That(circlePixels[i + 1], Is.LessThanOrEqualTo(circlePixels[i + 3]));
                Assert.That(circlePixels[i + 2], Is.LessThanOrEqualTo(circlePixels[i + 3]));
            }

            byte[] gradPixels = ReadbackRgba8(host.Device, context, gradTex.NativeTexture, 64, 64);
            int leftIdx = (32 * 64 + 2) * 4;
            int rightIdx = (32 * 64 + 61) * 4;
            Assert.That(gradPixels[leftIdx + 0], Is.GreaterThan((byte)220));
            Assert.That(gradPixels[leftIdx + 2], Is.LessThan((byte)35));
            Assert.That(gradPixels[rightIdx + 2], Is.GreaterThan((byte)220));
            Assert.That(gradPixels[rightIdx + 0], Is.LessThan((byte)35));
        }
        finally
        {
            context.Destroy();
        }
    }

    private static byte[] ReadbackRgba8(GPUDevice device, RenderContext context, Texture texture, uint width, uint height)
    {
        ulong byteCount = (ulong)width * height * 4;
        GPUHeap readback = device.AllocateGPUHeap(byteCount, MemoryType.Readback);
        try
        {
            context.BeginFrame();
            context.CopyTextureToMemory(texture.NativeTexture, readback.Range.GPURange);
            context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Host, Access.HostRead);
            context.Submit().Wait();

            byte[] result = new byte[byteCount];
            new ReadOnlySpan<byte>(readback.Range.Cpu, (int)byteCount).CopyTo(result);
            return result;
        }
        finally
        {
            readback.Destroy();
        }
    }

    private static void AssertPixel(byte[] pixels, int width, int x, int y, byte r, byte g, byte b, byte a)
    {
        int i = (y * width + x) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(pixels[i + 0], Is.InRange((byte)Math.Max(0, r - 1), (byte)Math.Min(255, r + 1)));
            Assert.That(pixels[i + 1], Is.InRange((byte)Math.Max(0, g - 1), (byte)Math.Min(255, g + 1)));
            Assert.That(pixels[i + 2], Is.InRange((byte)Math.Max(0, b - 1), (byte)Math.Min(255, b + 1)));
            Assert.That(pixels[i + 3], Is.InRange((byte)Math.Max(0, a - 1), (byte)Math.Min(255, a + 1)));
        });
    }
}
