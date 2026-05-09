using NUnit.Framework;

namespace Alco.Rendering.Test;

[TestFixture]
public unsafe class TestPremultiplyAlpha
{
    [Test]
    public void ZeroAlpha_ZeroesRGB()
    {
        byte[] pixels = [255, 128, 64, 0];

        fixed (byte* ptr = pixels)
            RenderingSystem.PremultiplyAlpha(ptr, 1);

        Assert.Multiple(() =>
        {
            Assert.That(pixels[0], Is.EqualTo(0));
            Assert.That(pixels[1], Is.EqualTo(0));
            Assert.That(pixels[2], Is.EqualTo(0));
            Assert.That(pixels[3], Is.EqualTo(0));
        });
    }

    [Test]
    public void FullAlpha_Unchanged()
    {
        byte[] pixels = [200, 100, 50, 255];

        fixed (byte* ptr = pixels)
            RenderingSystem.PremultiplyAlpha(ptr, 1);

        Assert.Multiple(() =>
        {
            Assert.That(pixels[0], Is.EqualTo(200));
            Assert.That(pixels[1], Is.EqualTo(100));
            Assert.That(pixels[2], Is.EqualTo(50));
            Assert.That(pixels[3], Is.EqualTo(255));
        });
    }

    [Test]
    public void PartialAlpha_PremultipliesRGB()
    {
        byte[] pixels = [255, 128, 64, 128];

        fixed (byte* ptr = pixels)
            RenderingSystem.PremultiplyAlpha(ptr, 1);

        Assert.Multiple(() =>
        {
            Assert.That(pixels[0], Is.EqualTo(128).Within(1));
            Assert.That(pixels[1], Is.EqualTo(64).Within(1));
            Assert.That(pixels[2], Is.EqualTo(32).Within(1));
            Assert.That(pixels[3], Is.EqualTo(128));
        });
    }

    [Test]
    public void AlphaChannel_AlwaysPreserved()
    {
        byte[] alphas = [0, 1, 64, 127, 128, 200, 254, 255];

        foreach (byte a in alphas)
        {
            byte[] pixels = [200, 150, 100, a];
            byte expectedA = a;

            fixed (byte* ptr = pixels)
                RenderingSystem.PremultiplyAlpha(ptr, 1);

            Assert.That(pixels[3], Is.EqualTo(expectedA), $"Alpha {a} not preserved");
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(17)]
    [TestCase(32)]
    [TestCase(100)]
    public void VariousPixelCounts_AllCorrect(int pixelCount)
    {
        byte[] pixels = new byte[pixelCount * 4];
        for (int i = 0; i < pixelCount; i++)
        {
            pixels[i * 4 + 0] = (byte)(200 + (i % 56));
            pixels[i * 4 + 1] = (byte)(100 + (i % 56));
            pixels[i * 4 + 2] = (byte)(50 + (i % 56));
            pixels[i * 4 + 3] = (byte)(i % 256);
        }

        byte[] expected = new byte[pixels.Length];
        for (int i = 0; i < pixelCount; i++)
        {
            int a = pixels[i * 4 + 3];
            expected[i * 4 + 0] = (byte)((pixels[i * 4 + 0] * a + 128) / 255);
            expected[i * 4 + 1] = (byte)((pixels[i * 4 + 1] * a + 128) / 255);
            expected[i * 4 + 2] = (byte)((pixels[i * 4 + 2] * a + 128) / 255);
            expected[i * 4 + 3] = (byte)a;
        }

        fixed (byte* ptr = pixels)
            RenderingSystem.PremultiplyAlpha(ptr, pixelCount);

        for (int i = 0; i < pixelCount; i++)
        {
            Assert.Multiple(() =>
            {
                Assert.That(pixels[i * 4 + 0], Is.EqualTo(expected[i * 4 + 0]).Within(1),
                    $"Pixel {i} R mismatch");
                Assert.That(pixels[i * 4 + 1], Is.EqualTo(expected[i * 4 + 1]).Within(1),
                    $"Pixel {i} G mismatch");
                Assert.That(pixels[i * 4 + 2], Is.EqualTo(expected[i * 4 + 2]).Within(1),
                    $"Pixel {i} B mismatch");
                Assert.That(pixels[i * 4 + 3], Is.EqualTo(expected[i * 4 + 3]),
                    $"Pixel {i} A not preserved");
            });
        }
    }

    [Test]
    public void AllPixelValues_RoundTrip()
    {
        for (int a = 0; a <= 255; a++)
        {
            for (int c = 0; c <= 255; c += 17)
            {
                byte[] pixels = [(byte)c, (byte)c, (byte)c, (byte)a];

                fixed (byte* ptr = pixels)
                    RenderingSystem.PremultiplyAlpha(ptr, 1);

                int expected = (c * a + 128) / 255;
                Assert.That(pixels[0], Is.EqualTo(expected).Within(1),
                    $"c={c}, a={a}: expected ~{expected}, got {pixels[0]}");
                Assert.That(pixels[3], Is.EqualTo(a), $"Alpha not preserved for a={a}");
            }
        }
    }
}
