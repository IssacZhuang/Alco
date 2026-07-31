using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Rendering;

namespace Alco.Benchmark;

/// <summary>
/// Profiling harness for image decode. Decodes each image type in a tight loop
/// to generate CPU profiling data via dotnet-trace.
/// </summary>
public static unsafe class ProfileImageDecode
{
    private const int DefaultIterations = 10_000;

    public static void Run(string[] args)
    {
        int iterations = DefaultIterations;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--iterations" && i + 1 < args.Length)
                iterations = int.Parse(args[i + 1]);
        }

        Console.WriteLine($"Image Decode Profiling Harness — {iterations:N0} iterations per workload");
        Console.WriteLine();

        string baseDir = AppContext.BaseDirectory;
        var workloads = new (string Name, string File, bool IsJpeg)[]
        {
            ("PNG Small", Path.Combine(baseDir, "Files/Image/png-small.png"), false),
            ("PNG Large", Path.Combine(baseDir, "Files/Image/png-large.png"), false),
            ("PNG Wall",  Path.Combine(baseDir, "Files/Image/wall.png"), false),
            ("JPEG",      Path.Combine(baseDir, "Files/Image/jpeg-real.jpg"), true),
        };

        foreach (var (name, file, isJpeg) in workloads)
        {
            byte[] data = File.ReadAllBytes(file);
            Console.WriteLine($"[{name}] {data.Length:N0} bytes, decoding {iterations:N0} times...");

            // Warmup: 100 iterations to ensure JIT has compiled everything
            for (int i = 0; i < 100; i++)
            {
                byte* ptr = isJpeg
                    ? ImageDecodeUtility.DecodeJpeg(data, out _, out _)
                    : ImageDecodeUtility.DecodePng(data, out _, out _);
                NativeMemory.Free(ptr);
            }

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                byte* ptr = isJpeg
                    ? ImageDecodeUtility.DecodeJpeg(data, out _, out _)
                    : ImageDecodeUtility.DecodePng(data, out _, out _);
                NativeMemory.Free(ptr);
            }
            sw.Stop();

            double usPerDecode = sw.Elapsed.TotalMicroseconds / iterations;
            Console.WriteLine($"  Total: {sw.ElapsedMilliseconds}ms, Per decode: {usPerDecode:F1}μs");
            Console.WriteLine();
        }

        Console.WriteLine("Profiling harness complete. Trace data captured.");
    }
}
