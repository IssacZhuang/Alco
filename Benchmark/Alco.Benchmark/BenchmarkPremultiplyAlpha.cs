using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkFramework;
using StbImageSharp;
using Alco.Rendering;

namespace Alco.Benchmark;

[Config(typeof(DefaultBenchmarkConfig))]
public unsafe class BenchmarkPremultiplyAlpha
{
    private byte[] _fileBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _fileBytes = File.ReadAllBytes("Files/test.png");
    }

    [Benchmark(Baseline = true, Description = "Decode only")]
    public void DecodeOnly()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_fileBytes, ColorComponents.RedGreenBlueAlpha);
    }

    [Benchmark(Description = "Decode + PremultiplyAlpha")]
    public void DecodeAndPremultiply()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_fileBytes, ColorComponents.RedGreenBlueAlpha);
        RenderingSystem.PremultiplyAlpha(image.UnsafePointer, image.Width * image.Height);
    }
}
