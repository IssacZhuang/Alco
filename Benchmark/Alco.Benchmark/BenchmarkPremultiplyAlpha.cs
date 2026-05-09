using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkFramework;
using StbImageSharp;
using Alco.Rendering;

namespace Alco.Benchmark;

[Config(typeof(DefaultBenchmarkConfig))]
public unsafe class BenchmarkPremultiplyAlpha
{
    private byte[] _originalData = null!;
    private byte[] _workData = null!;
    private int _pixelCount;

    [GlobalSetup]
    public void Setup()
    {
        byte[] fileBytes = File.ReadAllBytes("Files/test.png");
        using ImageResultBuffer image = ImageResultBuffer.FromMemory(fileBytes, ColorComponents.RedGreenBlueAlpha);
        _pixelCount = image.Width * image.Height;
        _originalData = new byte[image.Data.Length];
        image.Data.CopyTo(_originalData);
        _workData = new byte[_originalData.Length];
    }

    [IterationSetup]
    public void IterationSetup()
    {
        Array.Copy(_originalData, _workData, _originalData.Length);
    }

    [Benchmark(Baseline = true, Description = "No Premultiply")]
    public void NoPremultiply()
    {
        // Baseline: just the data copy (already done in IterationSetup)
    }

    [Benchmark(Description = "PremultiplyAlpha")]
    public void PremultiplyAlpha()
    {
        fixed (byte* ptr = _workData)
        {
            RenderingSystem.PremultiplyAlpha(ptr, _pixelCount);
        }
    }
}
