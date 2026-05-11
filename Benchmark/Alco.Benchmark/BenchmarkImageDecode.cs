using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkFramework;
using StbImageSharp;
using Alco.Rendering.Codec.Image;

namespace Alco.Benchmark;

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
