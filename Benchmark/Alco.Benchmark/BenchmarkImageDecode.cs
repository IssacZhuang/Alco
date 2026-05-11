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

    [Benchmark(Baseline = true, Description = "PNG small (STB)")]
    public void PngSmall_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_pngSmall, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "PNG small (new)")]
    public void PngSmall_New()
    {
        byte* ptr = ImageDecodeUtility.DecodePng(_pngSmall, out int w, out int h);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "PNG large (STB)")]
    public void PngLarge_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_pngLarge, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "PNG large (new)")]
    public void PngLarge_New()
    {
        byte* ptr = ImageDecodeUtility.DecodePng(_pngLarge, out int w, out int h);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "JPEG (STB)")]
    public void JpegReal_Stb()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_jpegReal, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "JPEG (new)")]
    public void JpegReal_New()
    {
        byte* ptr = ImageDecodeUtility.DecodeJpeg(_jpegReal, out int w, out int h);
        NativeMemory.Free(ptr);
    }
}
