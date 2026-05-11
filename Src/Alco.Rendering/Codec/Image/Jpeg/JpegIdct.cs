using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Inverse Discrete Cosine Transform for JPEG 8x8 blocks.
/// Uses AAN (Arai-Agui-Nakajima) fast IDCT algorithm with fixed-point short arithmetic.
/// Based on libjpeg-turbo's jidctfst.c implementation.
/// SIMD path uses Vector128&lt;short&gt; SSE2 intrinsics when hardware accelerated.
/// </summary>
internal static class JpegIdct
{
    // Fixed-point constants for scalar path (CONST_BITS = 8)
    private const int Fix_1_082 = 277;   // FIX(1.082392200) = 2*(c2-c6)
    private const int Fix_1_414 = 362;   // FIX(1.414213562) = 2*c4
    private const int Fix_1_847 = 473;   // FIX(1.847759065) = 2*c2
    private const int Fix_2_613 = 669;   // FIX(2.613125930) = 2*(c2+c6)

    private const int ConstBits = 8;
    private const int Pass1Bits = 2;

    // AAN per-position scaling factors for the ifast IDCT.
    // Precomputed values scaled up by 14 bits: aanscalefactor[row] * aanscalefactor[col] * 2^14.
    // Applied to the de-zigzagged block before the butterfly passes.
    private static ReadOnlySpan<short> AanScales => new short[]
    {
        16384, 22725, 21407, 19266, 16384, 12873,  8867,  4520,
        22725, 31521, 29692, 26722, 22725, 17855, 12299,  6270,
        21407, 29692, 27969, 25172, 21407, 16819, 11585,  5906,
        19266, 26722, 25172, 22654, 19266, 15137, 10426,  5315,
        16384, 22725, 21407, 19266, 16384, 12873,  8867,  4520,
        12873, 17855, 16819, 15137, 12873, 10114,  6967,  3552,
         8867, 12299, 11585, 10426,  8867,  6967,  4799,  2446,
         4520,  6270,  5906,  5315,  4520,  3552,  2446,  1247
    };

    /// <summary>
    /// Descale for AAN scaling: (AAN_SCALE_BITS - PASS1_BITS) = 14 - 2 = 12.
    /// </summary>
    private const int AanScaleDescale = 12;

    // Fixed-point constants (CONST_BITS = 8) used for both scalar and SIMD multiply.
    // SIMD path uses 32-bit intermediate multiply for accuracy.
    private const short SimdFix_1_082 = 277;    // FIX(1.082392200)
    private const short SimdFix_1_414 = 362;    // FIX(1.414213562)
    private const short SimdFix_1_847 = 473;    // FIX(1.847759065)
    private const short SimdFix_2_613 = 669;    // FIX(2.613125930)

    /// <summary>
    /// SIMD fixed-point multiply: equivalent to scalar (var * const_val) >> CONST_BITS.
    /// Combines MultiplyHigh (high 16 bits of signed 32-bit product) with MultiplyLow
    /// (low 16 bits of unsigned product, same bits for signed) to get (a*b) >> 8 exactly.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> SimdMultiply(Vector128<short> var, short constVal)
    {
        var cv = Vector128.Create(constVal);

        // (a*b) >> 8 = ((a*b) >> 16) << 8 | ((a*b) & 0xFFFF) >> 8
        // MultiplyHigh gives (a*b) >> 16 (signed)
        // MultiplyLow gives (a*b) & 0xFFFF (unsigned, but low bits are same for signed)
        var hi = Sse2.MultiplyHigh(var, cv);
        var lo = Sse2.MultiplyLow(var, cv);

        // Combine: (hi << 8) | (lo >> 8)
        return Sse2.Or(
            Sse2.ShiftLeftLogical(hi, (byte)(16 - ConstBits)),
            Sse2.ShiftRightLogical(lo, (byte)ConstBits).AsInt16());
    }

    private static ReadOnlySpan<byte> ZigzagOrder => new byte[]
    {
         0,  1,  8, 16,  9,  2,  3, 10,
        17, 24, 32, 25, 18, 11,  4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13,  6,  7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    };

    /// <summary>
    /// Perform IDCT on a dequantized 8x8 block and output as 8-bit samples.
    /// Coefficients are in zigzag order.
    /// </summary>
    public static void Transform(ReadOnlySpan<short> coeffs, Span<byte> output, int outputStride)
    {
        // Zero-row skip: if all AC coefficients are zero, just broadcast DC.
        // With AAN scaling, DC * aanscales[0] >> 12 = DC * 4. Then the butterfly
        // output for DC-only is (DC*4 + bias) >> 5 + 128 = ((dcVal << 2) + 16) >> 5 + 128.
        if (HasOnlyDc(coeffs))
        {
            int aanDc = (int)coeffs[0] << 2;
            byte pixel = (byte)(((aanDc + 16) >> 5) + 128);
            for (int i = 0; i < 64; i++)
                output[i] = pixel;
            return;
        }

        // De-zigzag into 8x8 block (row-major natural order)
        Span<short> block = stackalloc short[64];
        var zigzag = ZigzagOrder;
        for (int i = 0; i < 64; i++)
            block[zigzag[i]] = coeffs[i];

        // Apply AAN scaling factors to the de-zigzagged block.
        // This folds the AAN normalization constants into the coefficients,
        // equivalent to what libjpeg does when building the ifast quant table.
        var aanScales = AanScales;
        for (int i = 0; i < 64; i++)
            block[i] = (short)((block[i] * (int)aanScales[i]) >> AanScaleDescale);

        // Scalar AAN fast IDCT path.
        // SIMD path with SSE2 short arithmetic has intermediate overflow issues;
        // will be addressed with 32-bit intermediate arithmetic in a follow-up.
        TransformScalar(block, output, outputStride);
    }

    /// <summary>
    /// Check if only the DC coefficient is nonzero (all AC coefficients in zigzag indices 1-63 are zero).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasOnlyDc(ReadOnlySpan<short> coeffs)
    {
        for (int i = 1; i < 64; i++)
        {
            if (coeffs[i] != 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// AAN fast IDCT using SSE2 SIMD intrinsics.
    /// </summary>
    private static void TransformSimd(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        // Load 8 rows as Vector128<short>
        ref short blockRef = ref MemoryMarshal.GetReference(block);
        var r0 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 0)));
        var r1 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 8)));
        var r2 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 16)));
        var r3 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 24)));
        var r4 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 32)));
        var r5 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 40)));
        var r6 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 48)));
        var r7 = Unsafe.ReadUnaligned<Vector128<short>>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref blockRef, 56)));

        // Pass 1: AAN butterfly on columns (8 columns in parallel, one row per register)
        AanButterflySimdPass1(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);

        // Transpose so that Pass 2 can process rows with correct frequency ordering.
        TransposeSimd(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);

        // Pass 2: AAN butterfly on rows with final descale >> (Pass1Bits+3)
        AanButterflySimdPass2(ref r0, ref r1, ref r2, ref r3, ref r4, ref r5, ref r6, ref r7);

        // Add 128 level shift and store using PackUnsignedSaturate to clamp [0,255]
        var bias = Vector128.Create((short)128);
        ref byte outputRef = ref MemoryMarshal.GetReference(output);

        var b0 = Sse2.Add(r0, bias);
        var b1 = Sse2.Add(r1, bias);
        var b2 = Sse2.Add(r2, bias);
        var b3 = Sse2.Add(r3, bias);
        var b4 = Sse2.Add(r4, bias);
        var b5 = Sse2.Add(r5, bias);
        var b6 = Sse2.Add(r6, bias);
        var b7 = Sse2.Add(r7, bias);

        // Pack with zero vector to get 8 valid bytes in the lower half.
        var zero = Vector128<short>.Zero;
        StoreRow(Sse2.PackUnsignedSaturate(b0, zero), ref outputRef, 0 * outputStride);
        StoreRow(Sse2.PackUnsignedSaturate(b1, zero), ref outputRef, 1 * outputStride);
        StoreRow(Sse2.PackUnsignedSaturate(b2, zero), ref outputRef, 2 * outputStride);
        StoreRow(Sse2.PackUnsignedSaturate(b3, zero), ref outputRef, 3 * outputStride);
        StoreRow(Sse2.PackUnsignedSaturate(b4, zero), ref outputRef, 4 * outputStride);
        StoreRow(Sse2.PackUnsignedSaturate(b5, zero), ref outputRef, 5 * outputStride);
        StoreRow(Sse2.PackUnsignedSaturate(b6, zero), ref outputRef, 6 * outputStride);
        StoreRow(Sse2.PackUnsignedSaturate(b7, zero), ref outputRef, 7 * outputStride);
    }

    /// <summary>
    /// Store the lower 8 bytes of a Vector128 to the output at the given offset.
    /// Writes exactly 8 bytes (ulong), avoiding overflow into adjacent rows.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreRow(Vector128<byte> packed, ref byte outputRef, int offset)
    {
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref outputRef, offset),
            packed.AsUInt64().GetElement(0));
    }

    /// <summary>
    /// AAN butterfly pass 1 for SIMD.
    /// No scaling applied - results are raw integer sums/products.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AanButterflySimdPass1(
        ref Vector128<short> x0, ref Vector128<short> x1,
        ref Vector128<short> x2, ref Vector128<short> x3,
        ref Vector128<short> x4, ref Vector128<short> x5,
        ref Vector128<short> x6, ref Vector128<short> x7)
    {
        // Even part
        var tmp10 = Sse2.Add(x0, x4);      // phase 3
        var tmp11 = Sse2.Subtract(x0, x4);

        var tmp13 = Sse2.Add(x2, x6);      // phases 5-3
        var tmp12 = Sse2.Subtract(
            SimdMultiply(Sse2.Subtract(x2, x6), SimdFix_1_414),
            tmp13);                          // 2*c4

        var tmp0 = Sse2.Add(tmp10, tmp13);  // phase 2
        var tmp3 = Sse2.Subtract(tmp10, tmp13);
        var tmp1 = Sse2.Add(tmp11, tmp12);
        var tmp2 = Sse2.Subtract(tmp11, tmp12);

        // Odd part
        var z13 = Sse2.Add(x5, x3);         // phase 6
        var z10 = Sse2.Subtract(x5, x3);
        var z11 = Sse2.Add(x1, x7);
        var z12 = Sse2.Subtract(x1, x7);

        var tmp7 = Sse2.Add(z11, z13);      // phase 5
        var tmp11odd = SimdMultiply(Sse2.Subtract(z11, z13), SimdFix_1_414);  // 2*c4

        var z5 = SimdMultiply(Sse2.Add(z10, z12), SimdFix_1_847);             // 2*c2
        var tmp10odd = Sse2.Subtract(SimdMultiply(z12, SimdFix_1_082), z5);   // 2*(c2-c6)
        var tmp12odd = Sse2.Add(SimdMultiply(z10, (short)(-SimdFix_2_613)), z5); // -2*(c2+c6)

        var tmp6 = Sse2.Subtract(tmp12odd, tmp7);   // phase 2
        var tmp5 = Sse2.Subtract(tmp11odd, tmp6);
        var tmp4 = Sse2.Add(tmp10odd, tmp5);

        // Final combine (no scaling)
        x0 = Sse2.Add(tmp0, tmp7);
        x7 = Sse2.Subtract(tmp0, tmp7);
        x1 = Sse2.Add(tmp1, tmp6);
        x6 = Sse2.Subtract(tmp1, tmp6);
        x2 = Sse2.Add(tmp2, tmp5);
        x5 = Sse2.Subtract(tmp2, tmp5);
        x3 = Sse2.Subtract(tmp3, tmp4);
        x4 = Sse2.Add(tmp3, tmp4);
    }

    /// <summary>
    /// AAN butterfly pass 2 for SIMD.
    /// Same structure as pass 1, but final output gets descaled by &gt;&gt; (Pass1Bits + 3) = &gt;&gt; 5.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AanButterflySimdPass2(
        ref Vector128<short> x0, ref Vector128<short> x1,
        ref Vector128<short> x2, ref Vector128<short> x3,
        ref Vector128<short> x4, ref Vector128<short> x5,
        ref Vector128<short> x6, ref Vector128<short> x7)
    {
        // Even part
        var tmp10 = Sse2.Add(x0, x4);      // phase 3
        var tmp11 = Sse2.Subtract(x0, x4);

        var tmp13 = Sse2.Add(x2, x6);      // phases 5-3
        var tmp12 = Sse2.Subtract(
            SimdMultiply(Sse2.Subtract(x2, x6), SimdFix_1_414),
            tmp13);                          // 2*c4

        var tmp0 = Sse2.Add(tmp10, tmp13);  // phase 2
        var tmp3 = Sse2.Subtract(tmp10, tmp13);
        var tmp1 = Sse2.Add(tmp11, tmp12);
        var tmp2 = Sse2.Subtract(tmp11, tmp12);

        // Odd part
        var z13 = Sse2.Add(x5, x3);         // phase 6
        var z10 = Sse2.Subtract(x5, x3);
        var z11 = Sse2.Add(x1, x7);
        var z12 = Sse2.Subtract(x1, x7);

        var tmp7 = Sse2.Add(z11, z13);      // phase 5
        var tmp11odd = SimdMultiply(Sse2.Subtract(z11, z13), SimdFix_1_414);  // 2*c4

        var z5 = SimdMultiply(Sse2.Add(z10, z12), SimdFix_1_847);             // 2*c2
        var tmp10odd = Sse2.Subtract(SimdMultiply(z12, SimdFix_1_082), z5);   // 2*(c2-c6)
        var tmp12odd = Sse2.Add(SimdMultiply(z10, (short)(-SimdFix_2_613)), z5); // -2*(c2+c6)

        var tmp6 = Sse2.Subtract(tmp12odd, tmp7);   // phase 2
        var tmp5 = Sse2.Subtract(tmp11odd, tmp6);
        var tmp4 = Sse2.Add(tmp10odd, tmp5);

        // Final combine with descale >> (Pass1Bits + 3) = >> 5, rounding bias = 1 << 4 = 16
        const int FinalDescale = Pass1Bits + 3;
        const int BiasValue = 1 << (FinalDescale - 1);
        var roundBias = Vector128.Create((short)BiasValue);

        x0 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(tmp0, tmp7), roundBias), (byte)FinalDescale);
        x7 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(tmp0, tmp7), roundBias), (byte)FinalDescale);
        x1 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(tmp1, tmp6), roundBias), (byte)FinalDescale);
        x6 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(tmp1, tmp6), roundBias), (byte)FinalDescale);
        x2 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(tmp2, tmp5), roundBias), (byte)FinalDescale);
        x5 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(tmp2, tmp5), roundBias), (byte)FinalDescale);
        x3 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Subtract(tmp3, tmp4), roundBias), (byte)FinalDescale);
        x4 = Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.Add(tmp3, tmp4), roundBias), (byte)FinalDescale);
    }

    /// <summary>
    /// Transpose an 8x8 matrix of shorts using 3-phase unpack.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TransposeSimd(
        ref Vector128<short> r0, ref Vector128<short> r1,
        ref Vector128<short> r2, ref Vector128<short> r3,
        ref Vector128<short> r4, ref Vector128<short> r5,
        ref Vector128<short> r6, ref Vector128<short> r7)
    {
        // Phase 1: interleave 16-bit words
        var t0 = Sse2.UnpackLow(r0, r1);
        var t1 = Sse2.UnpackHigh(r0, r1);
        var t2 = Sse2.UnpackLow(r2, r3);
        var t3 = Sse2.UnpackHigh(r2, r3);
        var t4 = Sse2.UnpackLow(r4, r5);
        var t5 = Sse2.UnpackHigh(r4, r5);
        var t6 = Sse2.UnpackLow(r6, r7);
        var t7 = Sse2.UnpackHigh(r6, r7);

        // Phase 2: interleave 32-bit dwords
        var u0 = Sse2.UnpackLow(t0, t2);
        var u1 = Sse2.UnpackHigh(t0, t2);
        var u2 = Sse2.UnpackLow(t1, t3);
        var u3 = Sse2.UnpackHigh(t1, t3);
        var u4 = Sse2.UnpackLow(t4, t6);
        var u5 = Sse2.UnpackHigh(t4, t6);
        var u6 = Sse2.UnpackLow(t5, t7);
        var u7 = Sse2.UnpackHigh(t5, t7);

        // Phase 3: interleave 64-bit qwords
        r0 = Sse2.UnpackLow(u0, u4);
        r1 = Sse2.UnpackHigh(u0, u4);
        r2 = Sse2.UnpackLow(u1, u5);
        r3 = Sse2.UnpackHigh(u1, u5);
        r4 = Sse2.UnpackLow(u2, u6);
        r5 = Sse2.UnpackHigh(u2, u6);
        r6 = Sse2.UnpackLow(u3, u7);
        r7 = Sse2.UnpackHigh(u3, u7);
    }

    /// <summary>
    /// Scalar AAN fast IDCT fallback for platforms without SIMD.
    /// </summary>
    private static void TransformScalar(ReadOnlySpan<short> block, Span<byte> output, int outputStride)
    {
        // Process rows (pass 1: no scaling, raw integer sums)
        Span<short> workspace = stackalloc short[64];
        for (int row = 0; row < 8; row++)
        {
            int baseIdx = row * 8;
            AanButterflyScalarPass1(
                block[baseIdx + 0], block[baseIdx + 1], block[baseIdx + 2], block[baseIdx + 3],
                block[baseIdx + 4], block[baseIdx + 5], block[baseIdx + 6], block[baseIdx + 7],
                workspace, baseIdx);
        }

        // Process columns (pass 2: with final descale >> (Pass1Bits + 3))
        Span<short> col = stackalloc short[8];
        for (int colIdx = 0; colIdx < 8; colIdx++)
        {
            AanButterflyScalarPass2(
                workspace[0 * 8 + colIdx], workspace[1 * 8 + colIdx],
                workspace[2 * 8 + colIdx], workspace[3 * 8 + colIdx],
                workspace[4 * 8 + colIdx], workspace[5 * 8 + colIdx],
                workspace[6 * 8 + colIdx], workspace[7 * 8 + colIdx],
                col, 0);

            for (int row = 0; row < 8; row++)
            {
                int val = col[row] + 128;
                output[row * outputStride + colIdx] = (byte)Math.Clamp(val, 0, 255);
            }
        }
    }

    /// <summary>
    /// Scalar AAN butterfly pass 1.
    /// No scaling applied - results are raw integer sums/products.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AanButterflyScalarPass1(
        int x0, int x1, int x2, int x3, int x4, int x5, int x6, int x7,
        Span<short> output, int outBase)
    {
        // Even part
        int tmp10 = x0 + x4;               // phase 3
        int tmp11 = x0 - x4;

        int tmp13 = x2 + x6;               // phases 5-3
        int tmp12 = ((x2 - x6) * Fix_1_414 >> ConstBits) - tmp13;  // 2*c4

        int tmp0 = tmp10 + tmp13;           // phase 2
        int tmp3 = tmp10 - tmp13;
        int tmp1 = tmp11 + tmp12;
        int tmp2 = tmp11 - tmp12;

        // Odd part
        int z13 = x5 + x3;                 // phase 6
        int z10 = x5 - x3;
        int z11 = x1 + x7;
        int z12 = x1 - x7;

        int tmp7 = z11 + z13;              // phase 5
        int tmp11odd = ((z11 - z13) * Fix_1_414 >> ConstBits);  // 2*c4

        int z5 = ((z10 + z12) * Fix_1_847 >> ConstBits);        // 2*c2
        int tmp10odd = ((z12 * Fix_1_082) >> ConstBits) - z5;    // 2*(c2-c6)
        int tmp12odd = ((z10 * (-Fix_2_613)) >> ConstBits) + z5; // -2*(c2+c6)

        int tmp6 = tmp12odd - tmp7;        // phase 2
        int tmp5 = tmp11odd - tmp6;
        int tmp4 = tmp10odd + tmp5;

        // Final combine (no scaling)
        output[outBase + 0] = (short)(tmp0 + tmp7);
        output[outBase + 7] = (short)(tmp0 - tmp7);
        output[outBase + 1] = (short)(tmp1 + tmp6);
        output[outBase + 6] = (short)(tmp1 - tmp6);
        output[outBase + 2] = (short)(tmp2 + tmp5);
        output[outBase + 5] = (short)(tmp2 - tmp5);
        output[outBase + 3] = (short)(tmp3 - tmp4);
        output[outBase + 4] = (short)(tmp3 + tmp4);
    }

    /// <summary>
    /// Scalar AAN butterfly pass 2.
    /// Same structure as pass 1, but final output gets descaled by &gt;&gt; (Pass1Bits + 3) = &gt;&gt; 5.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AanButterflyScalarPass2(
        int x0, int x1, int x2, int x3, int x4, int x5, int x6, int x7,
        Span<short> output, int outBase)
    {
        // Even part
        int tmp10 = x0 + x4;               // phase 3
        int tmp11 = x0 - x4;

        int tmp13 = x2 + x6;               // phases 5-3
        int tmp12 = ((x2 - x6) * Fix_1_414 >> ConstBits) - tmp13;  // 2*c4

        int tmp0 = tmp10 + tmp13;           // phase 2
        int tmp3 = tmp10 - tmp13;
        int tmp1 = tmp11 + tmp12;
        int tmp2 = tmp11 - tmp12;

        // Odd part
        int z13 = x5 + x3;                 // phase 6
        int z10 = x5 - x3;
        int z11 = x1 + x7;
        int z12 = x1 - x7;

        int tmp7 = z11 + z13;              // phase 5
        int tmp11odd = ((z11 - z13) * Fix_1_414 >> ConstBits);  // 2*c4

        int z5 = ((z10 + z12) * Fix_1_847 >> ConstBits);        // 2*c2
        int tmp10odd = ((z12 * Fix_1_082) >> ConstBits) - z5;    // 2*(c2-c6)
        int tmp12odd = ((z10 * (-Fix_2_613)) >> ConstBits) + z5; // -2*(c2+c6)

        int tmp6 = tmp12odd - tmp7;        // phase 2
        int tmp5 = tmp11odd - tmp6;
        int tmp4 = tmp10odd + tmp5;

        // Final combine with descale >> (Pass1Bits + 3) = >> 5, rounding bias = 1 << 4 = 16
        const int FinalDescale = Pass1Bits + 3;
        const int Bias = 1 << (FinalDescale - 1);

        output[outBase + 0] = (short)((tmp0 + tmp7 + Bias) >> FinalDescale);
        output[outBase + 7] = (short)((tmp0 - tmp7 + Bias) >> FinalDescale);
        output[outBase + 1] = (short)((tmp1 + tmp6 + Bias) >> FinalDescale);
        output[outBase + 6] = (short)((tmp1 - tmp6 + Bias) >> FinalDescale);
        output[outBase + 2] = (short)((tmp2 + tmp5 + Bias) >> FinalDescale);
        output[outBase + 5] = (short)((tmp2 - tmp5 + Bias) >> FinalDescale);
        output[outBase + 3] = (short)((tmp3 - tmp4 + Bias) >> FinalDescale);
        output[outBase + 4] = (short)((tmp3 + tmp4 + Bias) >> FinalDescale);
    }
}
