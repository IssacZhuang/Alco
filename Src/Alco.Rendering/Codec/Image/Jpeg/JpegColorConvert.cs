using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Color space conversion for JPEG: YCbCr, Grayscale, CMYK, YCCK to RGBA8.
/// Uses portable SIMD for vectorized processing.
/// </summary>
internal static class JpegColorConvert
{
    // YCbCr conversion constants.
    private const float CrToR = 1.402f;
    private const float CbToG = 0.344136f;
    private const float CrToG = 0.714136f;
    private const float CbToB = 1.772f;
    private const float ChromaOffset = 128.0f;

    /// <summary>
    /// Convert YCbCr planes to RGBA8 output.
    /// Handles subsampled chroma planes by using per-pixel strides.
    /// </summary>
    public static unsafe void YCbCrToRgba(
        byte* yPlane, int yStride,
        byte* cbPlane, int cbStride,
        byte* crPlane, int crStride,
        byte* output, int outputStride,
        int width, int height,
        int hSub, int vSub)
    {
        if (Vector256.IsHardwareAccelerated)
        {
            YCbCrToRgbaVector256(yPlane, yStride, cbPlane, cbStride, crPlane, crStride,
                output, outputStride, width, height, hSub, vSub);
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            YCbCrToRgbaVector128(yPlane, yStride, cbPlane, cbStride, crPlane, crStride,
                output, outputStride, width, height, hSub, vSub);
        }
        else
        {
            YCbCrToRgbaScalar(yPlane, yStride, cbPlane, cbStride, crPlane, crStride,
                output, outputStride, width, height, hSub, vSub);
        }
    }

    /// <summary>
    /// Convert Grayscale plane to RGBA8 output.
    /// </summary>
    public static unsafe void GrayToRgba(
        byte* grayPlane, int grayStride,
        byte* output, int outputStride,
        int width, int height)
    {
        for (int row = 0; row < height; row++)
        {
            byte* gray = grayPlane + row * grayStride;
            byte* outRow = output + row * outputStride;

            int x = 0;

            // SIMD path: process 8 pixels at a time.
            if (Vector256.IsHardwareAccelerated && width >= 8)
            {
                int simdEnd = width & ~7;
                for (; x < simdEnd; x += 8)
                {
                    var g = LoadByte8AsFloat(gray, x);
                    ClampAndStoreRgba8(g, g, g, outRow, x);
                }
            }

            // Vector128 path: process 4 pixels at a time.
            if (Vector128.IsHardwareAccelerated && x + 4 <= width)
            {
                var g = LoadByte4AsFloat(gray, x);
                ClampAndStoreRgba4(g, g, g, outRow, x);
                x += 4;
            }

            // Scalar tail.
            for (; x < width; x++)
            {
                byte v = gray[x];
                int off = x * 4;
                outRow[off] = v;
                outRow[off + 1] = v;
                outRow[off + 2] = v;
                outRow[off + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Convert CMYK planes to RGBA8 output.
    /// </summary>
    public static unsafe void CmykToRgba(
        byte* cPlane, int cStride,
        byte* mPlane, int mStride,
        byte* yPlane, int yStride,
        byte* kPlane, int kStride,
        byte* output, int outputStride,
        int width, int height)
    {
        for (int row = 0; row < height; row++)
        {
            byte* c = cPlane + row * cStride;
            byte* m = mPlane + row * mStride;
            byte* yp = yPlane + row * yStride;
            byte* k = kPlane + row * kStride;
            byte* outRow = output + row * outputStride;

            int x = 0;

            // SIMD: process 8 pixels at a time.
            if (Vector256.IsHardwareAccelerated && width >= 8)
            {
                var v255 = Vector256.Create(255.0f);
                var v255Inv = Vector256.Create(1.0f / 255.0f);
                int simdEnd = width & ~7;

                for (; x < simdEnd; x += 8)
                {
                    // Load 8 bytes from each plane as floats.
                    var cV = LoadByte8AsFloat(c, x);
                    var mV = LoadByte8AsFloat(m, x);
                    var yV = LoadByte8AsFloat(yp, x);
                    var kV = LoadByte8AsFloat(k, x);

                    // K_inv = 255 - K
                    var kInv = v255 - kV;

                    // R = K_inv * (255 - C) / 255
                    // G = K_inv * (255 - M) / 255
                    // B = K_inv * (255 - Y) / 255
                    var r = kInv * (v255 - cV) * v255Inv;
                    var g = kInv * (v255 - mV) * v255Inv;
                    var b = kInv * (v255 - yV) * v255Inv;

                    ClampAndStoreRgba8(r, g, b, outRow, x);
                }
            }

            // Fallback: Vector128 for 4 pixels.
            if (Vector128.IsHardwareAccelerated && x + 4 <= width)
            {
                var v255 = Vector128.Create(255.0f);
                var v255Inv = Vector128.Create(1.0f / 255.0f);

                var cV = LoadByte4AsFloat(c, x);
                var mV = LoadByte4AsFloat(m, x);
                var yV = LoadByte4AsFloat(yp, x);
                var kV = LoadByte4AsFloat(k, x);

                var kInv = v255 - kV;
                var r = kInv * (v255 - cV) * v255Inv;
                var g = kInv * (v255 - mV) * v255Inv;
                var b = kInv * (v255 - yV) * v255Inv;

                ClampAndStoreRgba4(r, g, b, outRow, x);
                x += 4;
            }

            // Scalar tail.
            for (; x < width; x++)
            {
                int ki = 255 - k[x];
                int off = x * 4;
                outRow[off] = (byte)(ki * (255 - c[x]) / 255);
                outRow[off + 1] = (byte)(ki * (255 - m[x]) / 255);
                outRow[off + 2] = (byte)(ki * (255 - yp[x]) / 255);
                outRow[off + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Convert YCCK planes to RGBA8 output.
    /// Invert C/M/Y, then YCbCr->RGB with K channel.
    /// </summary>
    public static unsafe void YcckToRgba(
        byte* yPlane, int yStride,
        byte* cbPlane, int cbStride,
        byte* crPlane, int crStride,
        byte* kPlane, int kStride,
        byte* output, int outputStride,
        int width, int height)
    {
        for (int row = 0; row < height; row++)
        {
            byte* y = yPlane + row * yStride;
            byte* cb = cbPlane + row * cbStride;
            byte* cr = crPlane + row * crStride;
            byte* k = kPlane + row * kStride;
            byte* outRow = output + row * outputStride;

            int x = 0;

            // SIMD: process 8 pixels at a time.
            if (Vector256.IsHardwareAccelerated && width >= 8)
            {
                var v128 = Vector256.Create(ChromaOffset);
                var vCrR = Vector256.Create(CrToR);
                var vCbG = Vector256.Create(CbToG);
                var vCrG = Vector256.Create(CrToG);
                var vCbB = Vector256.Create(CbToB);
                var v255 = Vector256.Create(255.0f);
                var v255Inv = Vector256.Create(1.0f / 255.0f);
                var vZero = Vector256.Create(0.0f);
                var vMax = Vector256.Create(255.0f);
                var vHalf = Vector256.Create(0.5f);
                int simdEnd = width & ~7;

                for (; x < simdEnd; x += 8)
                {
                    var yV = LoadByte8AsFloat(y, x);
                    var cbV = LoadByte8AsFloat(cb, x);
                    var crV = LoadByte8AsFloat(cr, x);
                    var kV = LoadByte8AsFloat(k, x);

                    // YCbCr to RGB (standard conversion).
                    var cbOff = cbV - v128;
                    var crOff = crV - v128;

                    var rV = yV + vCrR * crOff;
                    var gV = yV - vCbG * cbOff - vCrG * crOff;
                    var bV = yV + vCbB * cbOff;

                    // Clamp to [0, 255] range.
                    rV = Vector256.Clamp(rV, vZero, vMax);
                    gV = Vector256.Clamp(gV, vZero, vMax);
                    bV = Vector256.Clamp(bV, vZero, vMax);

                    // Apply K channel: same as CMYK formula.
                    // R = K_inv * (255 - R) / 255 (invert R, then multiply by K inverse)
                    // But YCCK convention: invert C,M,Y means the YCbCr output IS the inverted component.
                    // So we do: R_out = (255 - K) * R / 255
                    var kInv = v255 - kV;
                    rV = kInv * rV * v255Inv;
                    gV = kInv * gV * v255Inv;
                    bV = kInv * bV * v255Inv;

                    rV = Vector256.Clamp(rV, vZero, vMax) + vHalf;
                    gV = Vector256.Clamp(gV, vZero, vMax) + vHalf;
                    bV = Vector256.Clamp(bV, vZero, vMax) + vHalf;

                    StoreRgba8(rV, gV, bV, outRow, x);
                }
            }

            // Scalar tail and non-SIMD path.
            for (; x < width; x++)
            {
                float yv = y[x];
                float cbv = cb[x] - ChromaOffset;
                float crv = cr[x] - ChromaOffset;

                float rv = yv + CrToR * crv;
                float gv = yv - CbToG * cbv - CrToG * crv;
                float bv = yv + CbToB * cbv;

                rv = Math.Clamp(rv, 0.0f, 255.0f);
                gv = Math.Clamp(gv, 0.0f, 255.0f);
                bv = Math.Clamp(bv, 0.0f, 255.0f);

                float ki = 255.0f - k[x];
                rv = ki * rv / 255.0f;
                gv = ki * gv / 255.0f;
                bv = ki * bv / 255.0f;

                int off = x * 4;
                outRow[off] = (byte)(Math.Clamp(rv, 0.0f, 255.0f) + 0.5f);
                outRow[off + 1] = (byte)(Math.Clamp(gv, 0.0f, 255.0f) + 0.5f);
                outRow[off + 2] = (byte)(Math.Clamp(bv, 0.0f, 255.0f) + 0.5f);
                outRow[off + 3] = 255;
            }
        }
    }

    // ---- YCbCr implementations ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void YCbCrToRgbaVector256(
        byte* yPlane, int yStride,
        byte* cbPlane, int cbStride,
        byte* crPlane, int crStride,
        byte* output, int outputStride,
        int width, int height,
        int hSub, int vSub)
    {
        var v128 = Vector256.Create(ChromaOffset);
        var vCrR = Vector256.Create(CrToR);
        var vCbG = Vector256.Create(CbToG);
        var vCrG = Vector256.Create(CrToG);
        var vCbB = Vector256.Create(CbToB);
        int simdWidth = width & ~7; // round down to multiple of 8

        for (int row = 0; row < height; row++)
        {
            byte* yRow = yPlane + row * yStride;
            byte* cbRow = cbPlane + (row / vSub) * cbStride;
            byte* crRow = crPlane + (row / vSub) * crStride;
            byte* outRow = output + row * outputStride;

            int x = 0;

            // Process 8 pixels at a time.
            for (; x < simdWidth; x += 8)
            {
                var yV = LoadByte8AsFloat(yRow, x);

                // Load chroma with subsampling: each chroma value may be shared across hSub pixels.
                // We need to handle subsampled chroma by looking up the correct chroma pixel.
                Vector256<float> cbV, crV;
                if (hSub == 1)
                {
                    cbV = LoadByte8AsFloat(cbRow, x);
                    crV = LoadByte8AsFloat(crRow, x);
                }
                else
                {
                    // hSub=2: replicate each chroma value to 2 output pixels.
                    cbV = LoadByte8AsFloatSubsampled(cbRow, x, hSub);
                    crV = LoadByte8AsFloatSubsampled(crRow, x, hSub);
                }

                var cbOff = cbV - v128;
                var crOff = crV - v128;

                var r = yV + vCrR * crOff;
                var g = yV - vCbG * cbOff - vCrG * crOff;
                var b = yV + vCbB * cbOff;

                ClampAndStoreRgba8(r, g, b, outRow, x);
            }

            // Scalar tail.
            YCbCrToRgbaTail(yRow, cbRow, crRow, outRow, x, width, hSub);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void YCbCrToRgbaVector128(
        byte* yPlane, int yStride,
        byte* cbPlane, int cbStride,
        byte* crPlane, int crStride,
        byte* output, int outputStride,
        int width, int height,
        int hSub, int vSub)
    {
        var v128 = Vector128.Create(ChromaOffset);
        var vCrR = Vector128.Create(CrToR);
        var vCbG = Vector128.Create(CbToG);
        var vCrG = Vector128.Create(CrToG);
        var vCbB = Vector128.Create(CbToB);
        int simdWidth = width & ~3; // round down to multiple of 4

        for (int row = 0; row < height; row++)
        {
            byte* yRow = yPlane + row * yStride;
            byte* cbRow = cbPlane + (row / vSub) * cbStride;
            byte* crRow = crPlane + (row / vSub) * crStride;
            byte* outRow = output + row * outputStride;

            int x = 0;

            for (; x < simdWidth; x += 4)
            {
                var yV = LoadByte4AsFloat(yRow, x);

                Vector128<float> cbV, crV;
                if (hSub == 1)
                {
                    cbV = LoadByte4AsFloat(cbRow, x);
                    crV = LoadByte4AsFloat(crRow, x);
                }
                else
                {
                    cbV = LoadByte4AsFloatSubsampled(cbRow, x, hSub);
                    crV = LoadByte4AsFloatSubsampled(crRow, x, hSub);
                }

                var cbOff = cbV - v128;
                var crOff = crV - v128;

                var r = yV + vCrR * crOff;
                var g = yV - vCbG * cbOff - vCrG * crOff;
                var b = yV + vCbB * cbOff;

                ClampAndStoreRgba4(r, g, b, outRow, x);
            }

            // Scalar tail.
            YCbCrToRgbaTail(yRow, cbRow, crRow, outRow, x, width, hSub);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void YCbCrToRgbaScalar(
        byte* yPlane, int yStride,
        byte* cbPlane, int cbStride,
        byte* crPlane, int crStride,
        byte* output, int outputStride,
        int width, int height,
        int hSub, int vSub)
    {
        for (int row = 0; row < height; row++)
        {
            byte* yRow = yPlane + row * yStride;
            byte* cbRow = cbPlane + (row / vSub) * cbStride;
            byte* crRow = crPlane + (row / vSub) * crStride;
            byte* outRow = output + row * outputStride;

            for (int x = 0; x < width; x++)
            {
                float yv = yRow[x];
                float cbv = cbRow[x / hSub] - ChromaOffset;
                float crv = crRow[x / hSub] - ChromaOffset;

                float rv = yv + CrToR * crv;
                float gv = yv - CbToG * cbv - CrToG * crv;
                float bv = yv + CbToB * cbv;

                int off = x * 4;
                outRow[off] = ClampByte(rv);
                outRow[off + 1] = ClampByte(gv);
                outRow[off + 2] = ClampByte(bv);
                outRow[off + 3] = 255;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void YCbCrToRgbaTail(
        byte* yRow, byte* cbRow, byte* crRow, byte* outRow,
        int startX, int width, int hSub)
    {
        for (int x = startX; x < width; x++)
        {
            float yv = yRow[x];
            float cbv = cbRow[x / hSub] - ChromaOffset;
            float crv = crRow[x / hSub] - ChromaOffset;

            float rv = yv + CrToR * crv;
            float gv = yv - CbToG * cbv - CrToG * crv;
            float bv = yv + CbToB * cbv;

            int off = x * 4;
            outRow[off] = ClampByte(rv);
            outRow[off + 1] = ClampByte(gv);
            outRow[off + 2] = ClampByte(bv);
            outRow[off + 3] = 255;
        }
    }

    // ---- SIMD load/store helpers ----

    /// <summary>
    /// Load 8 bytes from a pointer at the given offset and convert to Vector256 of float.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector256<float> LoadByte8AsFloat(byte* ptr, int offset)
    {
        var bytes = Unsafe.ReadUnaligned<Vector128<byte>>(ptr + offset);
        var u16 = Vector128.WidenLower(bytes);           // 8 bytes -> 8 ushort
        var u32Lo = Vector128.WidenLower(u16);           // lower 4 ushort -> 4 uint
        var u32Hi = Vector128.WidenUpper(u16);           // upper 4 ushort -> 4 uint
        var f32Lo = Vector128.ConvertToSingle(u32Lo);    // 4 uint -> 4 float
        var f32Hi = Vector128.ConvertToSingle(u32Hi);    // 4 uint -> 4 float
        return Vector256.Create(f32Lo, f32Hi);
    }

    /// <summary>
    /// Load 8 bytes from a subsampled chroma plane and expand to 8 floats.
    /// Each source byte covers hSub output pixels.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector256<float> LoadByte8AsFloatSubsampled(byte* ptr, int offset, int hSub)
    {
        // For hSub=2, we need 4 source bytes expanded to 8 output values.
        int srcOffset = offset / hSub;
        if (hSub == 2)
        {
            float c0 = ptr[srcOffset];
            float c1 = ptr[srcOffset + 1];
            float c2 = ptr[srcOffset + 2];
            float c3 = ptr[srcOffset + 3];
            return Vector256.Create(c0, c0, c1, c1, c2, c2, c3, c3);
        }

        // Generic fallback for other subsampling ratios.
        Span<float> tmp = stackalloc float[8];
        for (int i = 0; i < 8; i++)
            tmp[i] = ptr[srcOffset + i / hSub];
        return Vector256.Create(tmp[0], tmp[1], tmp[2], tmp[3],
                                tmp[4], tmp[5], tmp[6], tmp[7]);
    }

    /// <summary>
    /// Load 4 bytes from a pointer and convert to Vector128 of float.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<float> LoadByte4AsFloat(byte* ptr, int offset)
    {
        var bytes = Unsafe.ReadUnaligned<Vector128<byte>>(ptr + offset);
        var u16 = Vector128.WidenLower(bytes);
        var u32 = Vector128.WidenLower(u16);
        return Vector128.ConvertToSingle(u32);
    }

    /// <summary>
    /// Load 4 bytes from a subsampled chroma plane and expand to 4 floats.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe Vector128<float> LoadByte4AsFloatSubsampled(byte* ptr, int offset, int hSub)
    {
        int srcOffset = offset / hSub;
        if (hSub == 2)
        {
            float c0 = ptr[srcOffset];
            float c1 = ptr[srcOffset + 1];
            return Vector128.Create(c0, c0, c1, c1);
        }

        float s0 = ptr[srcOffset];
        float s1 = ptr[srcOffset + 1];
        float s2 = ptr[srcOffset + 2];
        float s3 = ptr[srcOffset + 3];
        return Vector128.Create(s0, s1, s2, s3);
    }

    /// <summary>
    /// Clamp R/G/B float vectors to [0,255], add 0.5 for rounding, and store as RGBA8.
    /// Processes 8 pixels.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ClampAndStoreRgba8(
        Vector256<float> r, Vector256<float> g, Vector256<float> b,
        byte* output, int offset)
    {
        var vZero = Vector256.Create(0.0f);
        var vMax = Vector256.Create(255.0f);
        var vHalf = Vector256.Create(0.5f);

        r = Vector256.Clamp(r, vZero, vMax) + vHalf;
        g = Vector256.Clamp(g, vZero, vMax) + vHalf;
        b = Vector256.Clamp(b, vZero, vMax) + vHalf;

        StoreRgba8(r, g, b, output, offset);
    }

    /// <summary>
    /// Store 8 RGBA pixels from R/G/B float vectors (alpha is 255).
    /// Uses vectorized pack+interleave to avoid per-element extraction.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void StoreRgba8(
        Vector256<float> r, Vector256<float> g, Vector256<float> b,
        byte* output, int offset)
    {
        var ri = Vector256.ConvertToInt32(r);
        var gi = Vector256.ConvertToInt32(g);
        var bi = Vector256.ConvertToInt32(b);

        // Narrow int32 -> int16 using portable Vector128.Narrow
        var r16 = Vector128.Narrow(ri.GetLower(), ri.GetUpper());
        var g16 = Vector128.Narrow(gi.GetLower(), gi.GetUpper());
        var b16 = Vector128.Narrow(bi.GetLower(), bi.GetUpper());
        var a16 = Vector128.Create((short)255);

        // Interleave R,G and B,A as short pairs using SSE2 UnpackLow/High (word-level)
        var rgLo = Sse2.UnpackLow(r16, g16);
        var rgHi = Sse2.UnpackHigh(r16, g16);
        var baLo = Sse2.UnpackLow(b16, a16);
        var baHi = Sse2.UnpackHigh(b16, a16);

        // Interleave RG and BA pairs at dword level for correct RGBA ordering
        var rgba0 = Sse2.UnpackLow(rgLo.AsInt32(), baLo.AsInt32());
        var rgba1 = Sse2.UnpackHigh(rgLo.AsInt32(), baLo.AsInt32());
        var rgba2 = Sse2.UnpackLow(rgHi.AsInt32(), baHi.AsInt32());
        var rgba3 = Sse2.UnpackHigh(rgHi.AsInt32(), baHi.AsInt32());

        // Narrow short -> byte with unsigned saturation
        var bytes01 = Vector128.NarrowWithSaturation(
            rgba0.AsUInt16(), rgba1.AsUInt16());
        var bytes23 = Vector128.NarrowWithSaturation(
            rgba2.AsUInt16(), rgba3.AsUInt16());

        Unsafe.WriteUnaligned(output + offset * 4, bytes01);
        Unsafe.WriteUnaligned(output + offset * 4 + 16, bytes23);
    }

    /// <summary>
    /// Clamp R/G/B float vectors to [0,255], add 0.5 for rounding, and store as RGBA8.
    /// Processes 4 pixels.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void ClampAndStoreRgba4(
        Vector128<float> r, Vector128<float> g, Vector128<float> b,
        byte* output, int offset)
    {
        var vZero = Vector128.Create(0.0f);
        var vMax = Vector128.Create(255.0f);
        var vHalf = Vector128.Create(0.5f);

        r = Vector128.Clamp(r, vZero, vMax) + vHalf;
        g = Vector128.Clamp(g, vZero, vMax) + vHalf;
        b = Vector128.Clamp(b, vZero, vMax) + vHalf;

        for (int i = 0; i < 4; i++)
        {
            int off = (offset + i) * 4;
            output[off] = (byte)r[i];
            output[off + 1] = (byte)g[i];
            output[off + 2] = (byte)b[i];
            output[off + 3] = 255;
        }
    }

    // ---- Utility helpers ----

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampByte(float value)
    {
        return (byte)(Math.Clamp(value, 0.0f, 255.0f) + 0.5f);
    }
}
