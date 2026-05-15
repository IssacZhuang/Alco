using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering.Codec.Image;

[TestFixture]
public unsafe class TestJpegColorConvert
{
    // ---- YCbCr tests ----

    /// <summary>
    /// Y=255, Cb=128, Cr=128 should produce pure white: R=255, G=255, B=255, A=255.
    /// </summary>
    [Test]
    public void TestYCbCrToRgba_PureWhite()
    {
        int width = 1, height = 1;
        byte[] yBuf = [255];
        byte[] cbBuf = [128];
        byte[] crBuf = [128];
        byte[] output = new byte[width * height * 4];

        fixed (byte* y = yBuf, cb = cbBuf, cr = crBuf, o = output)
        {
            JpegColorConvert.YCbCrToRgba(y, width, cb, 1, cr, 1, o, width * 4, width, height, 1, 1);
        }

        Assert.That(output[0], Is.EqualTo(255));   // R
        Assert.That(output[1], Is.EqualTo(255));   // G
        Assert.That(output[2], Is.EqualTo(255));   // B
        Assert.That(output[3], Is.EqualTo(255));   // A
    }

    /// <summary>
    /// Y=0, Cb=128, Cr=128 should produce pure black: R=0, G=0, B=0, A=255.
    /// </summary>
    [Test]
    public void TestYCbCrToRgba_PureBlack()
    {
        int width = 1, height = 1;
        byte[] yBuf = [0];
        byte[] cbBuf = [128];
        byte[] crBuf = [128];
        byte[] output = new byte[width * height * 4];

        fixed (byte* y = yBuf, cb = cbBuf, cr = crBuf, o = output)
        {
            JpegColorConvert.YCbCrToRgba(y, width, cb, 1, cr, 1, o, width * 4, width, height, 1, 1);
        }

        Assert.That(output[0], Is.EqualTo(0));     // R
        Assert.That(output[1], Is.EqualTo(0));     // G
        Assert.That(output[2], Is.EqualTo(0));     // B
        Assert.That(output[3], Is.EqualTo(255));   // A
    }

    /// <summary>
    /// Y=82, Cb=90, Cr=240 should produce a reddish color: R is high, G/B are low.
    /// R = 82 + 1.402*(240-128) = 82 + 156.624 = 238.624 -> 239
    /// G = 82 - 0.344136*(90-128) - 0.714136*(240-128) = 82 + 13.077 - 79.903 = 15.174 -> 15
    /// B = 82 + 1.772*(90-128) = 82 - 67.336 = 14.664 -> 15
    /// </summary>
    [Test]
    public void TestYCbCrToRgba_PureRed()
    {
        int width = 1, height = 1;
        byte[] yBuf = [82];
        byte[] cbBuf = [90];
        byte[] crBuf = [240];
        byte[] output = new byte[width * height * 4];

        fixed (byte* y = yBuf, cb = cbBuf, cr = crBuf, o = output)
        {
            JpegColorConvert.YCbCrToRgba(y, width, cb, 1, cr, 1, o, width * 4, width, height, 1, 1);
        }

        // R should be high (~239)
        Assert.That(output[0], Is.InRange(235, 255), "Red channel should be high");
        // G and B should be low
        Assert.That(output[1], Is.InRange(0, 30), "Green channel should be low");
        Assert.That(output[2], Is.InRange(0, 30), "Blue channel should be low");
        Assert.That(output[3], Is.EqualTo(255), "Alpha should be 255");
    }

    /// <summary>
    /// Test multiple known YCbCr values against hand-computed expected RGB.
    /// </summary>
    [Test]
    public void TestYCbCrToRgba_KnownValues()
    {
        // Compute expected via the formula:
        // R = Y + 1.402 * (Cr - 128)
        // G = Y - 0.344136 * (Cb - 128) - 0.714136 * (Cr - 128)
        // B = Y + 1.772 * (Cb - 128)
        int width = 4, height = 1;
        byte[] yBuf = [128, 200, 50, 160];
        byte[] cbBuf = [128, 100, 200, 128];
        byte[] crBuf = [128, 50, 100, 200];
        byte[] output = new byte[width * height * 4];

        fixed (byte* y = yBuf, cb = cbBuf, cr = crBuf, o = output)
        {
            JpegColorConvert.YCbCrToRgba(y, width, cb, 1, cr, 1, o, width * 4, width, height, 1, 1);
        }

        // Verify each pixel within +/-1 tolerance.
        for (int i = 0; i < width; i++)
        {
            float yv = yBuf[i];
            float cbv = cbBuf[i] - 128.0f;
            float crv = crBuf[i] - 128.0f;

            float expectedR = Math.Clamp(yv + 1.402f * crv, 0, 255);
            float expectedG = Math.Clamp(yv - 0.344136f * cbv - 0.714136f * crv, 0, 255);
            float expectedB = Math.Clamp(yv + 1.772f * cbv, 0, 255);

            int off = i * 4;
            Assert.That(output[off], Is.EqualTo((byte)(expectedR + 0.5f)).Within(1),
                $"Pixel {i} R");
            Assert.That(output[off + 1], Is.EqualTo((byte)(expectedG + 0.5f)).Within(1),
                $"Pixel {i} G");
            Assert.That(output[off + 2], Is.EqualTo((byte)(expectedB + 0.5f)).Within(1),
                $"Pixel {i} B");
            Assert.That(output[off + 3], Is.EqualTo(255),
                $"Pixel {i} A");
        }
    }

    /// <summary>
    /// Test 4:2:0 subsampling (hSub=2, vSub=2) on a 4x4 image.
    /// The chroma planes are 2x2 (one quarter resolution) and should be upsampled.
    /// </summary>
    [Test]
    public void TestYCbCrToRgba_Subsampled420()
    {
        int width = 4, height = 4;
        int hSub = 2, vSub = 2;

        // Y plane: 4x4 = 16 bytes
        byte[] yBuf = new byte[width * height];
        for (int i = 0; i < yBuf.Length; i++)
            yBuf[i] = 128;

        // Cb/Cr planes: 2x2 = 4 bytes each
        byte[] cbBuf = [100, 110, 120, 130];
        byte[] crBuf = [140, 150, 160, 170];

        byte[] output = new byte[width * height * 4];

        fixed (byte* y = yBuf, cb = cbBuf, cr = crBuf, o = output)
        {
            JpegColorConvert.YCbCrToRgba(y, width, cb, 2, cr, 2, o, width * 4, width, height, hSub, vSub);
        }

        // Verify that pixels in the same 2x2 block share the same chroma value.
        // Block (0,0)-(1,1) should use Cb[0]=100, Cr[0]=140
        // Block (2,0)-(3,1) should use Cb[1]=110, Cr[1]=150
        // Block (0,2)-(1,3) should use Cb[2]=120, Cr[2]=160
        // Block (2,2)-(3,3) should use Cb[3]=130, Cr[3]=170
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                float yv = 128;
                float cbv = cbBuf[(row / 2) * 2 + col / 2] - 128.0f;
                float crv = crBuf[(row / 2) * 2 + col / 2] - 128.0f;

                float expectedR = Math.Clamp(yv + 1.402f * crv, 0, 255);
                float expectedG = Math.Clamp(yv - 0.344136f * cbv - 0.714136f * crv, 0, 255);
                float expectedB = Math.Clamp(yv + 1.772f * cbv, 0, 255);

                int off = (row * width + col) * 4;
                Assert.That(output[off], Is.EqualTo((byte)(expectedR + 0.5f)).Within(1),
                    $"Pixel ({col},{row}) R");
                Assert.That(output[off + 1], Is.EqualTo((byte)(expectedG + 0.5f)).Within(1),
                    $"Pixel ({col},{row}) G");
                Assert.That(output[off + 2], Is.EqualTo((byte)(expectedB + 0.5f)).Within(1),
                    $"Pixel ({col},{row}) B");
                Assert.That(output[off + 3], Is.EqualTo(255),
                    $"Pixel ({col},{row}) A");
            }
        }
    }

    /// <summary>
    /// Test 9 pixels (not a multiple of 8) to verify SIMD tail handling.
    /// </summary>
    [Test]
    public void TestYCbCrToRgba_SimdBoundary()
    {
        int width = 9, height = 1;
        byte[] yBuf = new byte[width];
        byte[] cbBuf = new byte[width];
        byte[] crBuf = new byte[width];
        byte[] output = new byte[width * 4];

        // Fill with known pattern.
        for (int i = 0; i < width; i++)
        {
            yBuf[i] = (byte)(100 + i * 10);
            cbBuf[i] = (byte)(128 + i);
            crBuf[i] = (byte)(128 - i);
        }

        fixed (byte* y = yBuf, cb = cbBuf, cr = crBuf, o = output)
        {
            JpegColorConvert.YCbCrToRgba(y, width, cb, 1, cr, 1, o, width * 4, width, height, 1, 1);
        }

        // Verify each pixel against scalar formula.
        for (int i = 0; i < width; i++)
        {
            float yv = yBuf[i];
            float cbv = cbBuf[i] - 128.0f;
            float crv = crBuf[i] - 128.0f;

            float expectedR = Math.Clamp(yv + 1.402f * crv, 0, 255);
            float expectedG = Math.Clamp(yv - 0.344136f * cbv - 0.714136f * crv, 0, 255);
            float expectedB = Math.Clamp(yv + 1.772f * cbv, 0, 255);

            int off = i * 4;
            Assert.That(output[off], Is.EqualTo((byte)(expectedR + 0.5f)).Within(1),
                $"Pixel {i} R");
            Assert.That(output[off + 1], Is.EqualTo((byte)(expectedG + 0.5f)).Within(1),
                $"Pixel {i} G");
            Assert.That(output[off + 2], Is.EqualTo((byte)(expectedB + 0.5f)).Within(1),
                $"Pixel {i} B");
            Assert.That(output[off + 3], Is.EqualTo(255),
                $"Pixel {i} A");
        }
    }

    // ---- Grayscale tests ----

    /// <summary>
    /// Gray=128 should produce R=128, G=128, B=128, A=255.
    /// </summary>
    [Test]
    public void TestGrayToRgba_KnownValue()
    {
        int width = 1, height = 1;
        byte[] gray = [128];
        byte[] output = new byte[4];

        fixed (byte* g = gray, o = output)
        {
            JpegColorConvert.GrayToRgba(g, width, o, width * 4, width, height);
        }

        Assert.That(output[0], Is.EqualTo(128));   // R
        Assert.That(output[1], Is.EqualTo(128));   // G
        Assert.That(output[2], Is.EqualTo(128));   // B
        Assert.That(output[3], Is.EqualTo(255));   // A
    }

    /// <summary>
    /// Test various gray values: 0, 64, 128, 192, 255.
    /// </summary>
    [Test]
    public void TestGrayToRgba_Multiple()
    {
        int width = 5, height = 1;
        byte[] gray = [0, 64, 128, 192, 255];
        byte[] output = new byte[width * 4];

        fixed (byte* g = gray, o = output)
        {
            JpegColorConvert.GrayToRgba(g, width, o, width * 4, width, height);
        }

        for (int i = 0; i < width; i++)
        {
            int off = i * 4;
            Assert.That(output[off], Is.EqualTo(gray[i]), $"Pixel {i} R");
            Assert.That(output[off + 1], Is.EqualTo(gray[i]), $"Pixel {i} G");
            Assert.That(output[off + 2], Is.EqualTo(gray[i]), $"Pixel {i} B");
            Assert.That(output[off + 3], Is.EqualTo(255), $"Pixel {i} A");
        }
    }

    // ---- CMYK tests ----

    /// <summary>
    /// C=0, M=0, Y=0, K=0 should produce pure white: R=255, G=255, B=255.
    /// K_inv = 255, R = 255 * (255-0) / 255 = 255.
    /// </summary>
    [Test]
    public void TestCmykToRgba_KnownValue()
    {
        int width = 1, height = 1;
        byte[] cBuf = [0];
        byte[] mBuf = [0];
        byte[] yBuf = [0];
        byte[] kBuf = [0];
        byte[] output = new byte[4];

        fixed (byte* c = cBuf, m = mBuf, y = yBuf, k = kBuf, o = output)
        {
            JpegColorConvert.CmykToRgba(c, width, m, width, y, width, k, width, o, width * 4, width, height);
        }

        Assert.That(output[0], Is.EqualTo(255));   // R
        Assert.That(output[1], Is.EqualTo(255));   // G
        Assert.That(output[2], Is.EqualTo(255));   // B
        Assert.That(output[3], Is.EqualTo(255));   // A
    }

    /// <summary>
    /// K=255 should produce black regardless of C/M/Y values.
    /// K_inv = 0, so R/G/B all become 0.
    /// </summary>
    [Test]
    public void TestCmykToRgba_Black()
    {
        int width = 1, height = 1;
        byte[] cBuf = [100];
        byte[] mBuf = [200];
        byte[] yBuf = [50];
        byte[] kBuf = [255];
        byte[] output = new byte[4];

        fixed (byte* c = cBuf, m = mBuf, y = yBuf, k = kBuf, o = output)
        {
            JpegColorConvert.CmykToRgba(c, width, m, width, y, width, k, width, o, width * 4, width, height);
        }

        Assert.That(output[0], Is.EqualTo(0));     // R
        Assert.That(output[1], Is.EqualTo(0));     // G
        Assert.That(output[2], Is.EqualTo(0));     // B
        Assert.That(output[3], Is.EqualTo(255));   // A
    }
}
