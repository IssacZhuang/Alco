using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering.Codec.Image;

[TestFixture]
public class TestJpegIdct
{
    /// <summary>
    /// Test that a DC-only block (only coefficient[0] nonzero) produces a flat 8x8 block.
    /// The 2D IDCT of a pure DC term spreads evenly.
    /// f(x,y) = (1/4) * C(0)^2 * DC = DC / 8 for all pixels (C(0) = 1/sqrt(2)).
    /// Output = DC/8 + 128.
    /// </summary>
    [Test]
    public void TestDcOnlyBlock()
    {
        float[] coeffs = new float[64];
        coeffs[0] = 1000.0f; // DC coefficient in zigzag position 0

        byte[] output = new byte[64];
        JpegIdct.Transform(coeffs, output, 8);

        // DC-only: IDCT output = DC/8 + 128 = 1000/8 + 128 = 125 + 128 = 253
        byte expected = 253;
        for (int i = 0; i < 64; i++)
            Assert.That(output[i], Is.EqualTo(expected), $"Pixel {i}");
    }

    /// <summary>
    /// Test that an all-zeros block produces all 128 (level shift only).
    /// </summary>
    [Test]
    public void TestAllZeros()
    {
        float[] coeffs = new float[64];
        byte[] output = new byte[64];
        JpegIdct.Transform(coeffs, output, 8);

        for (int i = 0; i < 64; i++)
            Assert.That(output[i], Is.EqualTo(128), $"Pixel {i}");
    }

    /// <summary>
    /// Test a single AC coefficient at natural position [0,1] (zigzag index 1).
    /// The 2D IDCT with F(1,0)=C gives:
    /// f(x,y) = (1/4) * C(1)*C(0) * C * cos((2x+1)*pi/16) * cos(0)
    /// = C/(4*sqrt(2)) * cos((2x+1)*pi/16)
    /// </summary>
    [Test]
    public void TestSingleAc_CosinePattern()
    {
        float[] coeffs = new float[64];
        // Zigzag index 1 maps to natural position (row=0, col=1) = frequency (u=1, v=0)
        coeffs[1] = 200.0f;

        byte[] output = new byte[64];
        JpegIdct.Transform(coeffs, output, 8);

        float coefficient = 200.0f;
        float pi = MathF.PI;
        float sqrt2 = MathF.Sqrt(2);

        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                // 2D IDCT with single coefficient at (u=1, v=0):
                // f(x,y) = (1/4) * C(1)*C(0) * C * cos((2x+1)*pi/16) * cos(0)
                // C(1) = 1, C(0) = 1/sqrt(2), cos(0) = 1
                // = C/(4*sqrt(2)) * cos((2x+1)*pi/16)
                float expected = coefficient / (4.0f * sqrt2) * MathF.Cos((2 * x + 1) * pi / 16.0f) + 128.0f;
                int clamped = (int)Math.Clamp(Math.Round(expected), 0, 255);
                Assert.That(output[y * 8 + x], Is.EqualTo((byte)clamped).Within(1),
                    $"Pixel at (x={x}, y={y})");
            }
        }
    }

    /// <summary>
    /// Round-trip test: apply forward DCT to a known pixel pattern,
    /// then inverse DCT via JpegIdct.Transform. Verify output matches original within +/-1.
    /// Uses the standard orthonormal DCT-II/III pair with C(k) factors.
    /// </summary>
    [Test]
    public void TestIdentity_RoundTrip()
    {
        // Create a known 8x8 pixel pattern with varying values.
        byte[] original = new byte[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                original[y * 8 + x] = (byte)(64 + y * 16 + x * 8);

        // Forward DCT: F(u,v) = (1/4) * sum C(u)*C(v) * f(x,y) * cos * cos
        // This is the inverse of the IDCT which also uses (1/4) * C(u)*C(v) * cos * cos.
        float[] coeffs = new float[64];
        ForwardDct(original, coeffs);

        // Inverse DCT via JpegIdct
        byte[] output = new byte[64];
        JpegIdct.Transform(coeffs, output, 8);

        // Verify round-trip within tolerance
        for (int i = 0; i < 64; i++)
            Assert.That(output[i], Is.EqualTo(original[i]).Within(1), $"Pixel {i}");
    }

    /// <summary>
    /// Standard forward 2D DCT-II matching the IDCT definition.
    /// F(u,v) = (1/4) * sum_{x=0}^{7} sum_{y=0}^{7} C(u)*C(v) * f(x,y) * cos((2x+1)*u*pi/16) * cos((2y+1)*v*pi/16)
    /// where C(0) = 1/sqrt(2), C(k) = 1 for k > 0, and f(x,y) = pixel - 128.
    /// Produces coefficients in zigzag order.
    /// </summary>
    private static void ForwardDct(byte[] pixels, float[] coeffs)
    {
        float pi = MathF.PI;
        float sqrt2Inv = 1.0f / MathF.Sqrt(2);

        // Compute forward DCT in natural order
        float[] natural = new float[64];

        for (int v = 0; v < 8; v++)
        {
            for (int u = 0; u < 8; u++)
            {
                float cu = u == 0 ? sqrt2Inv : 1.0f;
                float cv = v == 0 ? sqrt2Inv : 1.0f;

                float sum = 0.0f;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        float pixel = pixels[y * 8 + x] - 128.0f;
                        sum += cu * cv * pixel
                            * MathF.Cos((2 * x + 1) * u * pi / 16.0f)
                            * MathF.Cos((2 * y + 1) * v * pi / 16.0f);
                    }
                }
                natural[v * 8 + u] = sum * 0.25f; // 1/4 scaling
            }
        }

        // Convert natural order to zigzag order.
        ReadOnlySpan<byte> zigzagOrder = new byte[]
        {
             0,  1,  5,  6, 14, 15, 27, 28,
             2,  4,  7, 13, 16, 26, 29, 42,
             3,  8, 12, 17, 25, 30, 41, 43,
             9, 11, 18, 24, 31, 40, 44, 53,
            10, 19, 23, 32, 39, 45, 52, 54,
            20, 22, 33, 38, 46, 51, 55, 60,
            21, 34, 37, 47, 50, 56, 59, 61,
            35, 36, 48, 49, 57, 58, 62, 63,
        };

        for (int i = 0; i < 64; i++)
            coeffs[i] = natural[zigzagOrder[i]];
    }
}
