using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Rendering.Codec.Image;
using StbImageSharp;

namespace Alco.Benchmark;

/// <summary>
/// Quick JPEG benchmark comparing STB vs New decoder without BenchmarkDotNet infrastructure.
/// </summary>
public static unsafe class QuickJpegBenchmark
{
    public static void Run()
    {
        byte[] jpegData = File.ReadAllBytes("Files/Image/jpeg-real.jpg");
        Console.WriteLine($"JPEG file size: {jpegData.Length:N0} bytes");

        // Warmup both decoders
        for (int i = 0; i < 200; i++)
        {
            using (ImageResultBuffer.FromMemory(jpegData, ColorComponents.RedGreenBlueAlpha)) { }
            byte* ptr = ImageDecodeUtility.DecodeJpeg(jpegData, out int w, out int h);
            NativeMemory.Free(ptr);
        }

        const int iterations = 5000;

        // STB benchmark
        long stbTotal = 0;
        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            using (ImageResultBuffer.FromMemory(jpegData, ColorComponents.RedGreenBlueAlpha)) { }
            stbTotal += Stopwatch.GetTimestamp() - start;
        }

        // New decoder benchmark
        long newTotal = 0;
        for (int i = 0; i < iterations; i++)
        {
            long start = Stopwatch.GetTimestamp();
            byte* ptr = ImageDecodeUtility.DecodeJpeg(jpegData, out int w, out int h);
            NativeMemory.Free(ptr);
            newTotal += Stopwatch.GetTimestamp() - start;
        }

        double freq = Stopwatch.Frequency;
        double stbMs = stbTotal / freq * 1000.0 / iterations;
        double newMs = newTotal / freq * 1000.0 / iterations;

        Console.WriteLine();
        Console.WriteLine($"Iterations: {iterations:N0}");
        Console.WriteLine($"STB decoder:   {stbMs:F3} ms/decode");
        Console.WriteLine($"New decoder:   {newMs:F3} ms/decode");
        Console.WriteLine($"Ratio (New/STB): {newMs / stbMs:F2}x");
        Console.WriteLine($"Speedup:       {(stbMs / newMs - 1) * 100:+0.0;-0.0}% (New vs STB)");
    }
}
