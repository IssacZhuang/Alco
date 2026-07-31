using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

[TestFixture]
public class TestPngAdam7
{
    #region GetPassSize tests

    [Test]
    public void TestGetPassSize_8x8()
    {
        // 8x8 image: all 7 passes should produce specific sub-image sizes
        Assert.That(PngAdam7.GetPassSize(0, 8, 8), Is.EqualTo((1, 1))); // Pass 1
        Assert.That(PngAdam7.GetPassSize(1, 8, 8), Is.EqualTo((1, 1))); // Pass 2
        Assert.That(PngAdam7.GetPassSize(2, 8, 8), Is.EqualTo((2, 1))); // Pass 3
        Assert.That(PngAdam7.GetPassSize(3, 8, 8), Is.EqualTo((2, 2))); // Pass 4
        Assert.That(PngAdam7.GetPassSize(4, 8, 8), Is.EqualTo((4, 2))); // Pass 5
        Assert.That(PngAdam7.GetPassSize(5, 8, 8), Is.EqualTo((4, 4))); // Pass 6
        Assert.That(PngAdam7.GetPassSize(6, 8, 8), Is.EqualTo((8, 4))); // Pass 7
    }

    [Test]
    public void TestGetPassSize_1x1()
    {
        // 1x1 image: only pass 1 has 1 pixel, all others are empty
        Assert.That(PngAdam7.GetPassSize(0, 1, 1), Is.EqualTo((1, 1))); // Pass 1
        Assert.That(PngAdam7.GetPassSize(1, 1, 1), Is.EqualTo((0, 0))); // Pass 2
        Assert.That(PngAdam7.GetPassSize(2, 1, 1), Is.EqualTo((0, 0))); // Pass 3
        Assert.That(PngAdam7.GetPassSize(3, 1, 1), Is.EqualTo((0, 0))); // Pass 4
        Assert.That(PngAdam7.GetPassSize(4, 1, 1), Is.EqualTo((0, 0))); // Pass 5
        Assert.That(PngAdam7.GetPassSize(5, 1, 1), Is.EqualTo((0, 0))); // Pass 6
        Assert.That(PngAdam7.GetPassSize(6, 1, 1), Is.EqualTo((0, 0))); // Pass 7
    }

    [Test]
    public void TestGetPassSize_7x7()
    {
        // Non-8-aligned image: 7x7
        // Pass 1: (0,0,8,8) -> w=(7-0+8-1)/8=1, h=(7-0+8-1)/8=1 -> (1,1)
        // Pass 2: (4,0,8,8) -> w=(7-4+8-1)/8=1, h=(7-0+8-1)/8=1 -> (1,1)
        // Pass 3: (0,4,4,8) -> w=(7-0+4-1)/4=2, h=(7-4+8-1)/8=1 -> (2,1)
        // Pass 4: (2,0,4,4) -> w=(7-2+4-1)/4=2, h=(7-0+4-1)/4=2 -> (2,2)
        // Pass 5: (0,2,2,4) -> w=(7-0+2-1)/2=4, h=(7-2+4-1)/4=2 -> (4,2)
        // Pass 6: (1,0,2,2) -> w=(7-1+2-1)/2=3, h=(7-0+2-1)/2=4 -> (3,4)
        // Pass 7: (0,1,1,2) -> w=(7-0+1-1)/1=7, h=(7-1+2-1)/2=3 -> (7,3)
        Assert.That(PngAdam7.GetPassSize(0, 7, 7), Is.EqualTo((1, 1)));
        Assert.That(PngAdam7.GetPassSize(1, 7, 7), Is.EqualTo((1, 1)));
        Assert.That(PngAdam7.GetPassSize(2, 7, 7), Is.EqualTo((2, 1)));
        Assert.That(PngAdam7.GetPassSize(3, 7, 7), Is.EqualTo((2, 2)));
        Assert.That(PngAdam7.GetPassSize(4, 7, 7), Is.EqualTo((4, 2)));
        Assert.That(PngAdam7.GetPassSize(5, 7, 7), Is.EqualTo((3, 4)));
        Assert.That(PngAdam7.GetPassSize(6, 7, 7), Is.EqualTo((7, 3)));
    }

    [Test]
    public void TestGetPassSize_16x16()
    {
        // Larger image: 16x16
        // Pass 1: (0,0,8,8) -> w=2, h=2
        // Pass 2: (4,0,8,8) -> w=2, h=2
        // Pass 3: (0,4,4,8) -> w=4, h=2
        // Pass 4: (2,0,4,4) -> w=4, h=4
        // Pass 5: (0,2,2,4) -> w=8, h=4
        // Pass 6: (1,0,2,2) -> w=8, h=8
        // Pass 7: (0,1,1,2) -> w=16, h=8
        Assert.That(PngAdam7.GetPassSize(0, 16, 16), Is.EqualTo((2, 2)));
        Assert.That(PngAdam7.GetPassSize(1, 16, 16), Is.EqualTo((2, 2)));
        Assert.That(PngAdam7.GetPassSize(2, 16, 16), Is.EqualTo((4, 2)));
        Assert.That(PngAdam7.GetPassSize(3, 16, 16), Is.EqualTo((4, 4)));
        Assert.That(PngAdam7.GetPassSize(4, 16, 16), Is.EqualTo((8, 4)));
        Assert.That(PngAdam7.GetPassSize(5, 16, 16), Is.EqualTo((8, 8)));
        Assert.That(PngAdam7.GetPassSize(6, 16, 16), Is.EqualTo((16, 8)));
    }

    #endregion

    #region MergePass tests

    /// <summary>
    /// Build pass scanline data with filter byte 0 (None) prepended to each row.
    /// </summary>
    private static byte[] BuildPassData(byte[] pixels, int passWidth, int passHeight, int bytesPerPixel)
    {
        int passRowBytes = passWidth * bytesPerPixel;
        int passStride = 1 + passRowBytes;
        byte[] passData = new byte[passHeight * passStride];

        for (int y = 0; y < passHeight; y++)
        {
            passData[y * passStride] = 0; // Filter byte = None
            Array.Copy(pixels, y * passRowBytes, passData, y * passStride + 1, passRowBytes);
        }

        return passData;
    }

    [Test]
    public unsafe void TestMergePass_8x8_AllPasses()
    {
        // Create a full 8x8 image where each pixel has a unique value (1 bpp)
        // Pixel values: pixel(x,y) = y * 8 + x + 1
        const int width = 8;
        const int height = 8;
        const int bpp = 1;
        int outputStride = width * bpp;
        byte[] output = new byte[width * height * bpp];

        // Generate pass data for each pass and merge
        for (int pass = 0; pass < 7; pass++)
        {
            var (passWidth, passHeight) = PngAdam7.GetPassSize(pass, width, height);
            Assert.That(passWidth, Is.GreaterThan(0), $"Pass {pass} should have width > 0 for 8x8 image");
            Assert.That(passHeight, Is.GreaterThan(0), $"Pass {pass} should have height > 0 for 8x8 image");

            var (startX, startY, incX, incY) = PngAdam7.PassParams[pass];

            // Create pixel data for this pass
            int passRowBytes = passWidth * bpp;
            byte[] passPixels = new byte[passHeight * passRowBytes];
            for (int y = 0; y < passHeight; y++)
            {
                for (int x = 0; x < passWidth; x++)
                {
                    int destX = startX + x * incX;
                    int destY = startY + y * incY;
                    passPixels[y * passRowBytes + x] = (byte)(destY * width + destX + 1);
                }
            }

            byte[] passData = BuildPassData(passPixels, passWidth, passHeight, bpp);
            int passStride = 1 + passRowBytes;

            fixed (byte* outputPtr = output)
            {
                PngAdam7.MergePass(outputPtr, outputStride, passData, passStride,
                    pass, width, height, bpp);
            }
        }

        // Verify all 64 pixels are in correct positions
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int expected = y * width + x + 1;
                Assert.That(output[y * outputStride + x], Is.EqualTo((byte)expected),
                    $"Mismatch at pixel ({x}, {y}): expected {expected}, got {output[y * outputStride + x]}");
            }
        }
    }

    [Test]
    public unsafe void TestMergePass_1x1()
    {
        // Single pixel image: only pass 1 has data
        const int width = 1;
        const int height = 1;
        const int bpp = 1;
        int outputStride = width * bpp;
        byte[] output = new byte[width * height * bpp];

        // Pass 1: (0,0,8,8) -> 1x1
        byte[] passPixels = [42];
        byte[] passData = BuildPassData(passPixels, 1, 1, bpp);
        int passStride = 1 + 1 * bpp;

        fixed (byte* outputPtr = output)
        {
            PngAdam7.MergePass(outputPtr, outputStride, passData, passStride,
                0, width, height, bpp);
        }

        Assert.That(output[0], Is.EqualTo(42));

        // Other passes should be no-ops (empty)
        for (int pass = 1; pass < 7; pass++)
        {
            fixed (byte* outputPtr = output)
            {
                // Merge with empty pass data should not crash or modify output
                PngAdam7.MergePass(outputPtr, outputStride, [], 0,
                    pass, width, height, bpp);
            }
        }

        // Output should still be just the single pixel from pass 1
        Assert.That(output[0], Is.EqualTo(42));
    }

    [Test]
    public unsafe void TestMergePass_3Bpp()
    {
        // RGB (3 bytes per pixel) on a 4x4 image
        // Only test passes that have data in a 4x4 image
        const int width = 4;
        const int height = 4;
        const int bpp = 3;
        int outputStride = width * bpp;
        byte[] output = new byte[width * height * bpp];

        // Expected pass sizes for 4x4:
        // Pass 1: (0,0,8,8) -> (1,1)
        // Pass 2: (4,0,8,8) -> (0,0) empty
        // Pass 3: (0,4,4,8) -> (1,0) empty (height 0)
        // Pass 4: (2,0,4,4) -> (1,1)
        // Pass 5: (0,2,2,4) -> (2,1)
        // Pass 6: (1,0,2,2) -> (2,2)
        // Pass 7: (0,1,1,2) -> (4,2)

        for (int pass = 0; pass < 7; pass++)
        {
            var (passWidth, passHeight) = PngAdam7.GetPassSize(pass, width, height);
            if (passWidth == 0 || passHeight == 0)
                continue;

            var (startX, startY, incX, incY) = PngAdam7.PassParams[pass];

            int passRowBytes = passWidth * bpp;
            byte[] passPixels = new byte[passHeight * passRowBytes];
            for (int y = 0; y < passHeight; y++)
            {
                for (int x = 0; x < passWidth; x++)
                {
                    int destX = startX + x * incX;
                    int destY = startY + y * incY;
                    // Encode pixel position into RGB: R=x, G=y, B=x+y*4
                    int offset = (y * passRowBytes + x * bpp);
                    passPixels[offset + 0] = (byte)destX;
                    passPixels[offset + 1] = (byte)destY;
                    passPixels[offset + 2] = (byte)(destX + destY * width);
                }
            }

            byte[] passData = BuildPassData(passPixels, passWidth, passHeight, bpp);
            int passStride = 1 + passRowBytes;

            fixed (byte* outputPtr = output)
            {
                PngAdam7.MergePass(outputPtr, outputStride, passData, passStride,
                    pass, width, height, bpp);
            }
        }

        // Verify all pixels in the 4x4 image
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int outOffset = y * outputStride + x * bpp;
                Assert.That(output[outOffset + 0], Is.EqualTo((byte)x),
                    $"R mismatch at pixel ({x}, {y})");
                Assert.That(output[outOffset + 1], Is.EqualTo((byte)y),
                    $"G mismatch at pixel ({x}, {y})");
                Assert.That(output[outOffset + 2], Is.EqualTo((byte)(x + y * width)),
                    $"B mismatch at pixel ({x}, {y})");
            }
        }
    }

    #endregion
}
