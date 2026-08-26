using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkFramework;
using StbImageWriteSharp;
using Alco.Rendering;

namespace Alco.Benchmark;

// ── Minimal P/Invoke bindings for libpng16 write ────────────────────────

file static unsafe class LibpngWriteNative
{
    const string Dll = "libpng16";

    public struct PngStruct { }
    public struct PngInfo { }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PngErrorPtr(PngStruct* png, byte* msg);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PngRwPtr(PngStruct* png, byte* data, nuint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PngFlushPtr(PngStruct* png);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern PngStruct* png_create_write_struct(
        IntPtr user_png_ver, IntPtr error_ptr, IntPtr error_fn, IntPtr warn_fn);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern PngInfo* png_create_info_struct(PngStruct* png);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_destroy_write_struct(PngStruct** png, PngInfo** info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_write_fn(
        PngStruct* png, void* io_ptr, PngRwPtr write_fn, PngFlushPtr flush_fn);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_IHDR(
        PngStruct* png, PngInfo* info,
        uint width, uint height, int bit_depth, int color_type,
        int interlace_method, int compression_method, int filter_method);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_write_info(PngStruct* png, PngInfo* info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_write_image(PngStruct* png, byte** row_pointers);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_write_end(PngStruct* png, PngInfo* info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void* png_get_io_ptr(PngStruct* png);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_compression_level(PngStruct* png, int level);
}

// ── Libpng encode helper ────────────────────────────────────────────────

file static unsafe class LibpngEncoder
{
    private static readonly LibpngWriteNative.PngErrorPtr _noopCallback = Noop;
    private static readonly LibpngWriteNative.PngRwPtr _writeCallback = WriteCallback;
    private static readonly LibpngWriteNative.PngFlushPtr _flushCallback = FlushCallback;
    private static readonly byte[] _verBytes = "1.6.47\0"u8.ToArray();

    private static void Noop(LibpngWriteNative.PngStruct* png, byte* msg) { }

    private struct LibpngOutputBuffer
    {
        public byte* Buffer;
        public int Length;
        public int Capacity;
    }

    private static void WriteCallback(LibpngWriteNative.PngStruct* png, byte* data, nuint length)
    {
        void* ioPtr = LibpngWriteNative.png_get_io_ptr(png);
        ref var stream = ref Unsafe.AsRef<LibpngOutputBuffer>(ioPtr);
        int len = (int)length;

        if (stream.Length + len > stream.Capacity)
        {
            int newCapacity = Math.Max(stream.Capacity * 2, stream.Length + len);
            byte* newBuf = (byte*)NativeMemory.Alloc((nuint)newCapacity);
            if (stream.Buffer != null)
            {
                Unsafe.CopyBlock(newBuf, stream.Buffer, (uint)stream.Length);
                NativeMemory.Free(stream.Buffer);
            }
            stream.Buffer = newBuf;
            stream.Capacity = newCapacity;
        }

        Unsafe.CopyBlock(stream.Buffer + stream.Length, data, (uint)len);
        stream.Length += len;
    }

    private static void FlushCallback(LibpngWriteNative.PngStruct* png) { }

    /// <summary>
    /// Encode RGBA8 pixels to PNG via libpng. Returns allocated buffer that caller must free.
    /// </summary>
    public static (IntPtr Ptr, int Length) Encode(byte* rgba, int width, int height)
    {
        var stream = new LibpngOutputBuffer { Buffer = null, Length = 0, Capacity = 0 };

        LibpngWriteNative.PngStruct* png;
        fixed (byte* pVer = _verBytes)
        {
            IntPtr pErrFn = Marshal.GetFunctionPointerForDelegate(_noopCallback);
            IntPtr pWarnFn = Marshal.GetFunctionPointerForDelegate(_noopCallback);
            png = LibpngWriteNative.png_create_write_struct(
                (IntPtr)pVer, IntPtr.Zero, pErrFn, pWarnFn);
        }

        LibpngWriteNative.PngInfo* info = LibpngWriteNative.png_create_info_struct(png);

        try
        {
            LibpngWriteNative.png_set_compression_level(png, 6); // Z_DEFAULT_COMPRESSION
            LibpngWriteNative.png_set_write_fn(png, Unsafe.AsPointer(ref stream), _writeCallback, _flushCallback);
            LibpngWriteNative.png_set_IHDR(png, info,
                (uint)width, (uint)height, 8, 6, // 8-bit RGBA
                0, 0, 0); // no interlace, default compression, default filter
            LibpngWriteNative.png_write_info(png, info);

            int stride = width * 4;
            byte** rowPtrs = (byte**)NativeMemory.Alloc((nuint)height, (nuint)sizeof(byte*));
            for (int i = 0; i < height; i++)
                rowPtrs[i] = rgba + i * stride;

            try
            {
                LibpngWriteNative.png_write_image(png, rowPtrs);
            }
            finally
            {
                NativeMemory.Free(rowPtrs);
            }

            LibpngWriteNative.png_write_end(png, info);
        }
        finally
        {
            LibpngWriteNative.png_destroy_write_struct(&png, &info);
        }

        return ((IntPtr)stream.Buffer, stream.Length);
    }
}

// ── STB encode helper (uses StbImageWriteSharp) ─────────────────────────

file static unsafe class StbEncoder
{
    public static byte[] Encode(byte* rgba, int width, int height)
    {
        using var ms = new MemoryStream();
        var writer = new ImageWriter();
        writer.WritePng(rgba, width, height, StbImageSharp.ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }
}

// ── Benchmark ───────────────────────────────────────────────────────────

[Config(typeof(DefaultBenchmarkConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class BenchmarkImageEncode
{
    // Decoded RGBA8 pixel data for each test image
    private byte[] _pixelsSmall = null!;
    private byte[] _pixelsLarge = null!;
    private byte[] _pixelsWall = null!;
    private int _wSmall, _hSmall;
    private int _wLarge, _hLarge;
    private int _wWall, _hWall;

    // Pinned pointers for libpng (avoids pinning per iteration)
    private GCHandle _pinSmall;
    private GCHandle _pinLarge;
    private GCHandle _pinWall;

    [GlobalSetup]
    public void Setup()
    {
        // Decoded once; reused read-only across iterations.
        (_pixelsSmall, _wSmall, _hSmall) = DecodeToPixels("Files/Image/png-small.png");
        (_pixelsLarge, _wLarge, _hLarge) = DecodeToPixels("Files/Image/png-large.png");
        (_pixelsWall, _wWall, _hWall) = DecodeToPixels("Files/Image/wall.png");

        // Pinned once; addresses stay valid across all iterations.
        _pinSmall = GCHandle.Alloc(_pixelsSmall, GCHandleType.Pinned);
        _pinLarge = GCHandle.Alloc(_pixelsLarge, GCHandleType.Pinned);
        _pinWall = GCHandle.Alloc(_pixelsWall, GCHandleType.Pinned);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pinSmall.Free();
        _pinLarge.Free();
        _pinWall.Free();
    }

    private static (byte[] Pixels, int W, int H) DecodeToPixels(string path)
    {
        byte[] fileData = File.ReadAllBytes(path);
        byte* ptr = ImageDecodeUtility.DecodePng(fileData, out int w, out int h);
        try
        {
            int totalBytes = w * h * 4;
            byte[] pixels = new byte[totalBytes];
            Unsafe.CopyBlock(Unsafe.AsPointer(ref pixels[0]), ptr, (uint)totalBytes);
            return (pixels, w, h);
        }
        finally
        {
            NativeMemory.Free(ptr);
        }
    }

    // ── PNG Small ─────────────────────────────────────────────────────────

    [Benchmark(Description = "New")]
    [BenchmarkCategory("PNG Small")]
    public void PngSmall_New()
    {
        byte[] png = ImageEncodeUtility.EncodePng(_pixelsSmall, _wSmall, _hSmall);
        GC.KeepAlive(png);
    }

    [Benchmark(Description = "Libpng")]
    [BenchmarkCategory("PNG Small")]
    public void PngSmall_Libpng()
    {
        byte* ptr = (byte*)_pinSmall.AddrOfPinnedObject();
        var (buf, _) = LibpngEncoder.Encode(ptr, _wSmall, _hSmall);
        NativeMemory.Free((byte*)buf);
    }

    [Benchmark(Description = "STB")]
    [BenchmarkCategory("PNG Small")]
    public void PngSmall_Stb()
    {
        fixed (byte* ptr = _pixelsSmall)
        {
            byte[] png = StbEncoder.Encode(ptr, _wSmall, _hSmall);
            GC.KeepAlive(png);
        }
    }

    // ── PNG Large ─────────────────────────────────────────────────────────

    [Benchmark(Description = "New")]
    [BenchmarkCategory("PNG Large")]
    public void PngLarge_New()
    {
        byte[] png = ImageEncodeUtility.EncodePng(_pixelsLarge, _wLarge, _hLarge);
        GC.KeepAlive(png);
    }

    [Benchmark(Description = "Libpng")]
    [BenchmarkCategory("PNG Large")]
    public void PngLarge_Libpng()
    {
        byte* ptr = (byte*)_pinLarge.AddrOfPinnedObject();
        var (buf, _) = LibpngEncoder.Encode(ptr, _wLarge, _hLarge);
        NativeMemory.Free((byte*)buf);
    }

    [Benchmark(Description = "STB")]
    [BenchmarkCategory("PNG Large")]
    public void PngLarge_Stb()
    {
        fixed (byte* ptr = _pixelsLarge)
        {
            byte[] png = StbEncoder.Encode(ptr, _wLarge, _hLarge);
            GC.KeepAlive(png);
        }
    }

    // ── PNG Wall ──────────────────────────────────────────────────────────

    [Benchmark(Description = "New")]
    [BenchmarkCategory("PNG Wall")]
    public void PngWall_New()
    {
        byte[] png = ImageEncodeUtility.EncodePng(_pixelsWall, _wWall, _hWall);
        GC.KeepAlive(png);
    }

    [Benchmark(Description = "Libpng")]
    [BenchmarkCategory("PNG Wall")]
    public void PngWall_Libpng()
    {
        byte* ptr = (byte*)_pinWall.AddrOfPinnedObject();
        var (buf, _) = LibpngEncoder.Encode(ptr, _wWall, _hWall);
        NativeMemory.Free((byte*)buf);
    }

    [Benchmark(Description = "STB")]
    [BenchmarkCategory("PNG Wall")]
    public void PngWall_Stb()
    {
        fixed (byte* ptr = _pixelsWall)
        {
            byte[] png = StbEncoder.Encode(ptr, _wWall, _hWall);
            GC.KeepAlive(png);
        }
    }
}
