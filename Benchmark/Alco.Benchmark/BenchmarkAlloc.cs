using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

public unsafe class BenchmarkAlloc
{
    [Benchmark(Description = "Marshal 16 bytes")]
    public void Marshal16b()
    {
        nint ptr = Marshal.AllocHGlobal(16);
        Marshal.FreeHGlobal(ptr);
    }

    [Benchmark(Description = "NativeMemory 16 bytes")]
    public void NativeMemory16b()
    {
        void* ptr = NativeMemory.Alloc(16);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "Marshal 1 kb")]
    public void Marshal1kb()
    {
        nint ptr = Marshal.AllocHGlobal(1024);
        Marshal.FreeHGlobal(ptr);
    }

    [Benchmark(Description = "NativeMemory 1 kb")]
    public void NativeMemory1kb()
    {
        void* ptr = NativeMemory.Alloc(1024);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "Marshal 1 mb")]
    public void Marshal1mb()
    {
        nint ptr = Marshal.AllocHGlobal(1024 * 1024);
        Marshal.FreeHGlobal(ptr);
    }

    [Benchmark(Description = "NativeMemory 1 mb")]
    public void NativeMemory1mb()
    {
        void* ptr = NativeMemory.Alloc(1024 * 1024);
        NativeMemory.Free(ptr);
    }

    [Benchmark(Description = "Marshal 16 mb")]
    public void Marshal16mb()
    {
        nint ptr = Marshal.AllocHGlobal(16 * 1024 * 1024);
        Marshal.FreeHGlobal(ptr);
    }

    [Benchmark(Description = "NativeMemory 16 mb")]
    public void NativeMemory16mb()
    {
        void* ptr = NativeMemory.Alloc(16 * 1024 * 1024);
        NativeMemory.Free(ptr);
    }
}
