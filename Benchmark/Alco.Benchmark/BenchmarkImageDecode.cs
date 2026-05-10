using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkFramework;
using StbImageSharp;
using Alco.Rendering.Codec.Image;

namespace Alco.Benchmark;

[Config(typeof(DefaultBenchmarkConfig))]
public unsafe class BenchmarkImageDecode
{
    private byte[] _pngSmall = null!;
    private byte[] _pngLarge = null!;
    private byte[] _jpegReal = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pngSmall = File.ReadAllBytes("Files/Image/png-small.png");
        _pngLarge = File.ReadAllBytes("Files/Image/png-large.png");
        _jpegReal = File.ReadAllBytes("Files/Image/jpeg-real.jpg");
    }

    // PNG Small (32x32 grayscale 8-bit)
    [Benchmark(Baseline = true, Description = "PNG small (STB)")]
    [BenchmarkCategory("PNG")]
    public void PngSmall_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_pngSmall, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "PNG small (new)")]
    [BenchmarkCategory("PNG")]
    public void PngSmall_New()
    {
        byte* ptr = ImageDecodeUtility.DecodePng(_pngSmall, out int w, out int h);
        NativeMemory.Free(ptr);
    }

    // PNG Large (z00n2c08, 32x32 RGB but highly compressed)
    [Benchmark(Baseline = true, Description = "PNG large (STB)")]
    [BenchmarkCategory("PNGLarge")]
    public void PngLarge_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_pngLarge, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "PNG large (new)")]
    [BenchmarkCategory("PNGLarge")]
    public void PngLarge_New()
    {
        byte* ptr = ImageDecodeUtility.DecodePng(_pngLarge, out int w, out int h);
        NativeMemory.Free(ptr);
    }

    // JPEG real-world (test.jpg, 144KB)
    [Benchmark(Baseline = true, Description = "JPEG (STB)")]
    [BenchmarkCategory("JPEG")]
    public void JpegReal_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_jpegReal, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "JPEG (new)")]
    [BenchmarkCategory("JPEG")]
    public void JpegReal_New()
    {
        byte* ptr = ImageDecodeUtility.DecodeJpeg(_jpegReal, out int w, out int h);
        NativeMemory.Free(ptr);
    }
}
