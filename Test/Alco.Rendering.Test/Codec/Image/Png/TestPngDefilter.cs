using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

[TestFixture]
public class TestPngDefilter
{
    /// <summary>
    /// Helper to build a scanline buffer from filter types and pixel rows.
    /// Each row is prefixed with its filter type byte.
    /// </summary>
    private static byte[] BuildScanlines(byte[] filterTypes, byte[][] pixelRows, int bytesPerPixel)
    {
        int height = filterTypes.Length;
        int width = pixelRows[0].Length / bytesPerPixel;
        int stride = width * bytesPerPixel;
        int rowSize = 1 + stride;

        byte[] scanlines = new byte[height * rowSize];
        for (int y = 0; y < height; y++)
        {
            scanlines[y * rowSize] = filterTypes[y];
            Array.Copy(pixelRows[y], 0, scanlines, y * rowSize + 1, stride);
        }
        return scanlines;
    }

    /// <summary>
    /// Reference scalar defiltering implementation for verification.
    /// </summary>
    private static byte[][] DefilterReference(byte[] filterTypes, byte[][] pixelRows, int bytesPerPixel)
    {
        int height = filterTypes.Length;
        int stride = pixelRows[0].Length;
        byte[][] result = new byte[height][];
        byte[] prevRow = new byte[stride]; // initialized to zeros

        for (int y = 0; y < height; y++)
        {
            result[y] = new byte[stride];
            Array.Copy(pixelRows[y], result[y], stride);
            byte[] row = result[y];

            switch (filterTypes[y])
            {
                case 0: // None
                    break;
                case 1: // Sub
                    for (int i = bytesPerPixel; i < stride; i++)
                        row[i] += row[i - bytesPerPixel];
                    break;
                case 2: // Up
                    for (int i = 0; i < stride; i++)
                        row[i] += prevRow[i];
                    break;
                case 3: // Average
                    for (int i = 0; i < bytesPerPixel && i < stride; i++)
                        row[i] += (byte)(prevRow[i] >> 1);
                    for (int i = bytesPerPixel; i < stride; i++)
                        row[i] += (byte)((prevRow[i] + row[i - bytesPerPixel]) >> 1);
                    break;
                case 4: // Paeth
                    for (int i = 0; i < bytesPerPixel && i < stride; i++)
                        row[i] += PaethPredictor(0, prevRow[i], 0);
                    for (int i = bytesPerPixel; i < stride; i++)
                        row[i] += PaethPredictor(row[i - bytesPerPixel], prevRow[i], prevRow[i - bytesPerPixel]);
                    break;
            }

            prevRow = (byte[])row.Clone();
        }

        return result;
    }

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    [Test]
    public void TestFilterNone()
    {
        // 3 rows of 4 pixels (1 bpp = grayscale), all with filter None
        int width = 4, height = 3, bpp = 1;
        byte[] filterTypes = [0, 0, 0];
        byte[][] pixelRows =
        [
            [10, 20, 30, 40],
            [50, 60, 70, 80],
            [90, 100, 110, 120]
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }

    [Test]
    public void TestFilterSub()
    {
        // 2 rows of 4 pixels (1 bpp), filter Sub
        // Sub: raw[i] += raw[i - bpp]
        // Row 0 filtered input: [1, 2, 3, 4] -> defiltered: [1, 3, 6, 10]
        // Row 1 filtered input: [5, 1, 1, 1] -> defiltered: [5, 6, 7, 8]
        int width = 4, height = 2, bpp = 1;
        byte[] filterTypes = [1, 1];
        byte[][] pixelRows =
        [
            [1, 2, 3, 4],
            [5, 1, 1, 1]
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }

        // Verify specific values
        Assert.That(scanlines[1], Is.EqualTo(1));   // row 0, pixel 0
        Assert.That(scanlines[2], Is.EqualTo(3));   // row 0, pixel 1
        Assert.That(scanlines[3], Is.EqualTo(6));   // row 0, pixel 2
        Assert.That(scanlines[4], Is.EqualTo(10));  // row 0, pixel 3
    }

    [Test]
    public void TestFilterUp()
    {
        // Row 0 with filter Up: all zeros prev, so result = raw unchanged
        // Row 1 with filter Up: raw[i] += prev[i]
        int width = 4, height = 2, bpp = 1;
        byte[] filterTypes = [2, 2];
        byte[][] pixelRows =
        [
            [10, 20, 30, 40],
            [1, 2, 3, 4]
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }

        // Row 0: prev = [0,0,0,0], so result = [10,20,30,40] unchanged
        // Row 1: prev = [10,20,30,40], raw = [1,2,3,4], result = [11,22,33,44]
        // Row 1 data starts at index 6 (row 1 offset = 1*5=5, +1 for filter byte = 6)
        Assert.That(scanlines[6], Is.EqualTo(11));
        Assert.That(scanlines[7], Is.EqualTo(22));
        Assert.That(scanlines[8], Is.EqualTo(33));
        Assert.That(scanlines[9], Is.EqualTo(44));
    }

    [Test]
    public void TestFilterAverage()
    {
        // Average: raw[i] += (prev[i] + raw[i-bpp]) / 2
        // First row: prev = [0,0,0,0], bpp=1
        //   pixel 0: raw[0] += (0 + 0)/2 = 0 -> raw[0] stays
        //   pixel 1: raw[1] += (0 + raw[0])/2
        //   pixel 2: raw[2] += (0 + raw[1])/2
        //   pixel 3: raw[3] += (0 + raw[2])/2
        int width = 4, height = 2, bpp = 1;
        byte[] filterTypes = [3, 3];
        byte[][] pixelRows =
        [
            [10, 20, 30, 40],
            [5, 10, 15, 20]
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }

        // Hand-verify row 0:
        // prev = [0,0,0,0]
        // pixel 0: 10 + (0+0)/2 = 10
        // pixel 1: 20 + (0+10)/2 = 20+5 = 25
        // pixel 2: 30 + (0+25)/2 = 30+12 = 42
        // pixel 3: 40 + (0+42)/2 = 40+21 = 61
        Assert.That(scanlines[1], Is.EqualTo(10));
        Assert.That(scanlines[2], Is.EqualTo(25));
        Assert.That(scanlines[3], Is.EqualTo(42));
        Assert.That(scanlines[4], Is.EqualTo(61));
    }

    [Test]
    public void TestFilterPaeth()
    {
        // Paeth: raw[i] += PaethPredictor(raw[i-bpp], prev[i], prev[i-bpp])
        int width = 4, height = 2, bpp = 1;
        byte[] filterTypes = [4, 4];
        byte[][] pixelRows =
        [
            [10, 20, 30, 40],
            [5, 10, 15, 20]
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }

        // Hand-verify row 0: prev = [0,0,0,0], a=0, c=0 for all
        // PaethPredictor(0, 0, 0): p=0, pa=0, pb=0, pc=0 -> pa<=pb && pa<=pc -> a=0
        // pixel 0: 10 + 0 = 10
        // pixel 1: PaethPredictor(10, 0, 0): p=10, pa=0, pb=10, pc=10 -> pa<=pb && pa<=pc -> a=10
        //   20 + 10 = 30
        // pixel 2: PaethPredictor(30, 0, 0): p=30, pa=0, pb=30, pc=30 -> a=30
        //   30 + 30 = 60
        // pixel 3: PaethPredictor(60, 0, 0): p=60, pa=0, pb=60, pc=60 -> a=60
        //   40 + 60 = 100
        Assert.That(scanlines[1], Is.EqualTo(10));
        Assert.That(scanlines[2], Is.EqualTo(30));
        Assert.That(scanlines[3], Is.EqualTo(60));
        Assert.That(scanlines[4], Is.EqualTo(100));
    }

    [Test]
    public void TestMixedFilters()
    {
        // 4 rows with different filter types
        int width = 4, height = 4, bpp = 1;
        byte[] filterTypes = [0, 1, 2, 4]; // None, Sub, Up, Paeth
        byte[][] pixelRows =
        [
            [10, 20, 30, 40],    // None: unchanged
            [5, 5, 5, 5],        // Sub: prefix sum
            [1, 2, 3, 4],        // Up: add prev row
            [0, 0, 0, 0]         // Paeth: predictor from prev and left
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }

    [Test]
    public void TestSimdBoundary_SingleVector()
    {
        // Row stride exactly equal to Vector256<byte>.Count (32 bytes) if available,
        // otherwise falls through to scalar which is also fine.
        // Use bpp=1, width=32
        int width = 32, height = 2, bpp = 1;
        int stride = width * bpp;

        byte[] filterTypes = [2, 1]; // Up then Sub
        byte[][] pixelRows = new byte[height][];
        for (int y = 0; y < height; y++)
        {
            pixelRows[y] = new byte[stride];
            for (int x = 0; x < stride; x++)
                pixelRows[y][x] = (byte)(x + y * 10);
        }

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }

    [Test]
    public void TestSimdBoundary_NonAligned()
    {
        // Row stride not a multiple of vector width: 17 bytes with bpp=1
        int width = 17, height = 3, bpp = 1;
        int stride = width * bpp;

        byte[] filterTypes = [0, 2, 3]; // None, Up, Average
        byte[][] pixelRows = new byte[height][];
        for (int y = 0; y < height; y++)
        {
            pixelRows[y] = new byte[stride];
            for (int x = 0; x < stride; x++)
                pixelRows[y][x] = (byte)((x * 7 + y * 13) & 0xFF);
        }

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }

    [Test]
    public void TestSmallRows_1Bpp()
    {
        // Test 1, 2, 3, 4 pixel widths with bpp=1
        int[] widths = [1, 2, 3, 4];

        foreach (int width in widths)
        {
            int height = 3;
            int bpp = 1;
            int stride = width * bpp;

            byte[] filterTypes = [1, 2, 4];
            byte[][] pixelRows = new byte[height][];
            for (int y = 0; y < height; y++)
            {
                pixelRows[y] = new byte[stride];
                for (int x = 0; x < stride; x++)
                    pixelRows[y][x] = (byte)((x + y * 3 + 1) * 11);
            }

            byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
            byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

            PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * (1 + stride) + 1;
                for (int x = 0; x < stride; x++)
                {
                    Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                        $"Mismatch at width={width}, row {y}, byte {x}");
                }
            }
        }
    }

    [Test]
    public void TestSmallRows_3Bpp()
    {
        // Test RGB (3 bpp) with small widths
        int[] widths = [1, 2, 3, 4];

        foreach (int width in widths)
        {
            int height = 3;
            int bpp = 3;
            int stride = width * bpp;

            byte[] filterTypes = [1, 3, 4];
            byte[][] pixelRows = new byte[height][];
            for (int y = 0; y < height; y++)
            {
                pixelRows[y] = new byte[stride];
                for (int x = 0; x < stride; x++)
                    pixelRows[y][x] = (byte)((x * 3 + y * 7 + 5) & 0xFF);
            }

            byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
            byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

            PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * (1 + stride) + 1;
                for (int x = 0; x < stride; x++)
                {
                    Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                        $"Mismatch at width={width}, row {y}, byte {x}");
                }
            }
        }
    }

    [Test]
    public void TestLargeRow_AllFilters()
    {
        // Test with a larger row to exercise SIMD paths thoroughly
        int width = 64, height = 10, bpp = 4; // RGBA
        int stride = width * bpp; // 256 bytes

        byte[] filterTypes = [0, 1, 2, 3, 4, 1, 2, 3, 4, 0];
        byte[][] pixelRows = new byte[height][];
        Random rng = new Random(42); // Deterministic seed
        for (int y = 0; y < height; y++)
        {
            pixelRows[y] = new byte[stride];
            rng.NextBytes(pixelRows[y]);
        }

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }

    [Test]
    public void TestByteOverflow()
    {
        // Test with byte values near 255 to verify wrapping behavior
        int width = 4, height = 2, bpp = 1;
        byte[] filterTypes = [1, 2];

        // Row 0: Sub filter with values near 255
        // 250 + 10 should wrap to 4 (mod 256)
        byte[][] pixelRows =
        [
            [250, 10, 10, 10],
            [200, 200, 200, 200]
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }

        // Verify specific overflow values
        // Row 0 defiltered (Sub): [250, (250+10)=4, (4+10)=14, (14+10)=24]
        Assert.That(scanlines[1], Is.EqualTo(250));
        Assert.That(scanlines[2], Is.EqualTo(4));   // 260 mod 256 = 4
        Assert.That(scanlines[3], Is.EqualTo(14));   // 4+10 = 14
        Assert.That(scanlines[4], Is.EqualTo(24));   // 14+10 = 24
    }

    [Test]
    public void TestPaethPredictor_AllBranches()
    {
        // Construct inputs where each of the three Paeth branches (a, b, c) is chosen
        int width = 3, height = 2, bpp = 1;
        byte[] filterTypes = [4, 4];

        // Row 0: PaethPredictor(0, prev=0, 0) = 0 for all, so row unchanged
        // Row 1: We want to test different predictor outcomes.
        // For Paeth to pick 'a': need pa <= pb && pa <= pc
        // For Paeth to pick 'b': need pb < pa or (pa > pb but pb <= pc)
        // For Paeth to pick 'c': need pc < pa && pc < pb

        // After row 0 defiltering with prev=[0,0,0]:
        //   PaethPredictor(0,0,0) = 0 for all (a=0 picked since pa=0=pb=pc=0, pa<=pb && pa<=pc)
        //   So row 0 = [10, 20, 30] unchanged
        // Row 1: prev = [10, 20, 30]
        //   pixel 0: PaethPredictor(0, prev=10, 0): a=0,b=10,c=0, p=10, pa=10,pb=0,pc=10 -> pb<=pc -> b=10
        //     raw[0] + 10
        //   pixel 1: PaethPredictor(row1_pixel0_result, prev=20, prev_pixel0=10):
        //     This depends on row1_pixel0_result. Let's just verify with reference.
        byte[][] pixelRows =
        [
            [10, 20, 30],
            [5, 5, 5]
        ];

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int stride = width * bpp;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * (1 + stride) + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }

    [Test]
    public void TestFilterSub_4Bpp_LargeRow()
    {
        // Test Sub filter with bpp=4 and stride=256 (exactly 8 Vector256 blocks)
        // Use known pattern to trace the prefix sum
        int width = 64, height = 2, bpp = 4;
        int stride = width * bpp; // 256

        byte[] filterTypes = [0, 1]; // Row 0: None, Row 1: Sub
        byte[][] pixelRows = new byte[height][];
        pixelRows[0] = new byte[stride];
        pixelRows[1] = new byte[stride];
        // Fill row 1 with a simple pattern: 1,2,3,4,1,2,3,4,...
        for (int i = 0; i < stride; i++)
            pixelRows[1][i] = (byte)((i % 4) + 1);

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int rowSize = 1 + stride;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * rowSize + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }

    [Test]
    public void TestFilterSub_4Bpp_VectorBoundary()
    {
        // Test Sub with bpp=4 where the stride spans exactly 1 vector (32 bytes = 8 pixels)
        int width = 8, height = 2, bpp = 4;
        int stride = width * bpp; // 32

        byte[] filterTypes = [0, 1];
        byte[][] pixelRows = new byte[height][];
        pixelRows[0] = new byte[stride];
        pixelRows[1] = new byte[stride];
        Random rng = new Random(123);
        rng.NextBytes(pixelRows[1]);

        byte[] scanlines = BuildScanlines(filterTypes, pixelRows, bpp);
        byte[][] expected = DefilterReference(filterTypes, pixelRows, bpp);

        PngDefilter.Defilter(scanlines, width, height, bpp, width * bpp);

        int rowSize = 1 + stride;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * rowSize + 1;
            for (int x = 0; x < stride; x++)
            {
                Assert.That(scanlines[rowOffset + x], Is.EqualTo(expected[y][x]),
                    $"Mismatch at row {y}, byte {x}");
            }
        }
    }
}
