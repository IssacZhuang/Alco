using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkFramework;
using StbImageSharp;
using Alco.Rendering.Codec.Image;

namespace Alco.Benchmark;

// ── Minimal P/Invoke bindings for libpng16.dll ──────────────────────────

file static unsafe class LibpngNative
{
    const string Dll = "libpng16";

    public struct PngStruct { }
    public struct PngInfo { }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PngErrorPtr(PngStruct* png, byte* msg);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PngRwPtr(PngStruct* png, byte* data, nuint length);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern PngStruct* png_create_read_struct(
        IntPtr user_png_ver, IntPtr error_ptr, IntPtr error_fn, IntPtr warn_fn);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern PngInfo* png_create_info_struct(PngStruct* png);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_destroy_read_struct(
        PngStruct** png, PngInfo** info, PngInfo** end);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_sig_bytes(PngStruct* png, int num_bytes);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_read_fn(
        PngStruct* png, void* io_ptr, PngRwPtr read_fn);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_read_info(PngStruct* png, PngInfo* info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_expand(PngStruct* png);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_strip_16(PngStruct* png);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_gray_to_rgb(PngStruct* png);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_set_add_alpha(PngStruct* png, uint value, int filler);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_read_update_info(PngStruct* png, PngInfo* info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint png_get_image_height(PngStruct* png, PngInfo* info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint png_get_rowbytes(PngStruct* png, PngInfo* info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_read_image(PngStruct* png, byte** image);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void png_read_end(PngStruct* png, PngInfo* info);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void* png_get_io_ptr(PngStruct* png);
}

// ── Libpng decode helper ────────────────────────────────────────────────

file static unsafe class LibpngDecoder
{
    private static readonly LibpngNative.PngErrorPtr _noopCallback = Noop;
    private static readonly LibpngNative.PngRwPtr _readCallback = ReadCallback;
    private static readonly byte[] _verBytes = "1.6.47\0"u8.ToArray();

    private static void Noop(LibpngNative.PngStruct* png, byte* msg) { }

    private struct MemoryReader
    {
        public GCHandle Handle;
        public int Offset;
        public int Length;
    }

    private static void ReadCallback(LibpngNative.PngStruct* png, byte* data, nuint length)
    {
        void* ioPtr = LibpngNative.png_get_io_ptr(png);
        ref var reader = ref Unsafe.AsRef<MemoryReader>(ioPtr);
        int len = (int)length;
        byte* src = (byte*)reader.Handle.AddrOfPinnedObject() + reader.Offset;
        Unsafe.CopyBlock(data, src, (uint)len);
        reader.Offset += len;
    }

    public static void Decode(byte[] pngData)
    {
        var reader = new MemoryReader
        {
            Handle = GCHandle.Alloc(pngData, GCHandleType.Pinned),
            Offset = 8,
            Length = pngData.Length
        };

        LibpngNative.PngStruct* png;
        fixed (byte* pVer = _verBytes)
        {
            IntPtr pErrFn = Marshal.GetFunctionPointerForDelegate(_noopCallback);
            IntPtr pWarnFn = Marshal.GetFunctionPointerForDelegate(_noopCallback);
            png = LibpngNative.png_create_read_struct(
                (IntPtr)pVer, IntPtr.Zero, pErrFn, pWarnFn);
        }

        LibpngNative.PngInfo* info = LibpngNative.png_create_info_struct(png);

        try
        {
            LibpngNative.png_set_sig_bytes(png, 8);
            LibpngNative.png_set_read_fn(png, Unsafe.AsPointer(ref reader), _readCallback);
            LibpngNative.png_read_info(png, info);

            LibpngNative.png_set_expand(png);
            LibpngNative.png_set_strip_16(png);
            LibpngNative.png_set_gray_to_rgb(png);
            LibpngNative.png_set_add_alpha(png, 0xFF, 1);
            LibpngNative.png_read_update_info(png, info);

            uint height = LibpngNative.png_get_image_height(png, info);
            nuint rowBytes = LibpngNative.png_get_rowbytes(png, info);

            byte** rowPtrs = (byte**)NativeMemory.Alloc((nuint)height, (nuint)sizeof(byte*));
            for (uint i = 0; i < height; i++)
                rowPtrs[i] = (byte*)NativeMemory.Alloc(rowBytes, 1);

            try
            {
                LibpngNative.png_read_image(png, rowPtrs);
                LibpngNative.png_read_end(png, info);
            }
            finally
            {
                for (uint i = 0; i < height; i++)
                    NativeMemory.Free(rowPtrs[i]);
                NativeMemory.Free(rowPtrs);
            }
        }
        finally
        {
            LibpngNative.png_destroy_read_struct(&png, &info, null);
            reader.Handle.Free();
        }
    }
}

// ── Benchmark ───────────────────────────────────────────────────────────

[Config(typeof(DefaultBenchmarkConfig))]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public unsafe class BenchmarkImageDecode
{
    private byte[] _pngSmall = null!;
    private byte[] _pngLarge = null!;
    private byte[] _pngWall = null!;
    private byte[] _jpegReal = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pngSmall = File.ReadAllBytes("Files/Image/png-small.png");
        _pngLarge = File.ReadAllBytes("Files/Image/png-large.png");
        _pngWall = File.ReadAllBytes("Files/Image/wall.png");
        _jpegReal = File.ReadAllBytes("Files/Image/jpeg-real.jpg");
    }

    // ── PNG Small ─────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "STB")]
    [BenchmarkCategory("PNG Small")]
    public void PngSmall_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_pngSmall, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "New")]
    [BenchmarkCategory("PNG Small")]
    public void PngSmall_New()
    {
        byte* ptr = ImageDecodeUtility.DecodePng(_pngSmall, out int w, out int h);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "Libpng")]
    [BenchmarkCategory("PNG Small")]
    public void PngSmall_Libpng()
    {
        LibpngDecoder.Decode(_pngSmall);
    }

    // ── PNG Large ─────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "STB")]
    [BenchmarkCategory("PNG Large")]
    public void PngLarge_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_pngLarge, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "New")]
    [BenchmarkCategory("PNG Large")]
    public void PngLarge_New()
    {
        byte* ptr = ImageDecodeUtility.DecodePng(_pngLarge, out int w, out int h);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "Libpng")]
    [BenchmarkCategory("PNG Large")]
    public void PngLarge_Libpng()
    {
        LibpngDecoder.Decode(_pngLarge);
    }

    // ── PNG Wall ──────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "STB")]
    [BenchmarkCategory("PNG Wall")]
    public void PngWall_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_pngWall, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "New")]
    [BenchmarkCategory("PNG Wall")]
    public void PngWall_New()
    {
        byte* ptr = ImageDecodeUtility.DecodePng(_pngWall, out int w, out int h);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "Libpng")]
    [BenchmarkCategory("PNG Wall")]
    public void PngWall_Libpng()
    {
        LibpngDecoder.Decode(_pngWall);
    }

    // ── JPEG ──────────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "STB")]
    [BenchmarkCategory("JPEG")]
    public void JpegReal_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_jpegReal, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "New")]
    [BenchmarkCategory("JPEG")]
    public void JpegReal_New()
    {
        byte* ptr = ImageDecodeUtility.DecodeJpeg(_jpegReal, out int w, out int h);
        NativeMemory.Free(ptr);
    }
}
