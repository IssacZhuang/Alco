using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkFramework;
using StbImageSharp;
using Alco.Rendering;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks image decoding with and without alpha premultiplication.
/// </summary>
[Config(typeof(DefaultBenchmarkConfig))]
public unsafe class BenchmarkPremultiplyAlpha
{
    private byte[] _fileBytes = null!;

    /// <summary>
    /// Loads the test image bytes.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _fileBytes = File.ReadAllBytes("Files/test.png");
    }

    /// <summary>
    /// Benchmarks decoding a PNG image only.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Decode only")]
    public void DecodeOnly()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_fileBytes, ColorComponents.RedGreenBlueAlpha);
    }

    /// <summary>
    /// Benchmarks decoding a PNG image followed by alpha premultiplication.
    /// </summary>
    [Benchmark(Description = "Decode + PremultiplyAlpha")]
    public void DecodeAndPremultiply()
    {
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(_fileBytes, ColorComponents.RedGreenBlueAlpha);
        RenderingSystem.PremultiplyAlpha(image.UnsafePointer, image.Width * image.Height);
    }
}
