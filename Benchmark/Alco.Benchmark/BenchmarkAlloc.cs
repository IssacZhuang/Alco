using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks unmanaged allocation and release with Marshal.AllocHGlobal and NativeMemory at several block sizes.
/// </summary>
public unsafe class BenchmarkAlloc
{
    /// <summary>
    /// Benchmarks allocating and freeing a 16-byte block with Marshal.
    /// </summary>
    [Benchmark(Description = "Marshal 16 bytes")]
    public void Marshal16b()
    {
        nint ptr = Marshal.AllocHGlobal(16);
        Marshal.FreeHGlobal(ptr);
    }

    /// <summary>
    /// Benchmarks allocating and freeing a 16-byte block with NativeMemory.
    /// </summary>
    [Benchmark(Description = "NativeMemory 16 bytes")]
    public void NativeMemory16b()
    {
        void* ptr = NativeMemory.Alloc(16);
        NativeMemory.Free(ptr);
    }

    /// <summary>
    /// Benchmarks allocating and freeing a 1 KB block with Marshal.
    /// </summary>
    [Benchmark(Description = "Marshal 1 kb")]
    public void Marshal1kb()
    {
        nint ptr = Marshal.AllocHGlobal(1024);
        Marshal.FreeHGlobal(ptr);
    }

    /// <summary>
    /// Benchmarks allocating and freeing a 1 KB block with NativeMemory.
    /// </summary>
    [Benchmark(Description = "NativeMemory 1 kb")]
    public void NativeMemory1kb()
    {
        void* ptr = NativeMemory.Alloc(1024);
        NativeMemory.Free(ptr);
    }

    /// <summary>
    /// Benchmarks allocating and freeing a 1 MB block with Marshal.
    /// </summary>
    [Benchmark(Description = "Marshal 1 mb")]
    public void Marshal1mb()
    {
        nint ptr = Marshal.AllocHGlobal(1024 * 1024);
        Marshal.FreeHGlobal(ptr);
    }

    /// <summary>
    /// Benchmarks allocating and freeing a 1 MB block with NativeMemory.
    /// </summary>
    [Benchmark(Description = "NativeMemory 1 mb")]
    public void NativeMemory1mb()
    {
        void* ptr = NativeMemory.Alloc(1024 * 1024);
        NativeMemory.Free(ptr);
    }

    /// <summary>
    /// Benchmarks allocating and freeing a 16 MB block with Marshal.
    /// </summary>
    [Benchmark(Description = "Marshal 16 mb")]
    public void Marshal16mb()
    {
        nint ptr = Marshal.AllocHGlobal(16 * 1024 * 1024);
        Marshal.FreeHGlobal(ptr);
    }

    /// <summary>
    /// Benchmarks allocating and freeing a 16 MB block with NativeMemory.
    /// </summary>
    [Benchmark(Description = "NativeMemory 16 mb")]
    public void NativeMemory16mb()
    {
        void* ptr = NativeMemory.Alloc(16 * 1024 * 1024);
        NativeMemory.Free(ptr);
    }
}
