using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

public class BenchmarkSIMD
{
    private const int ArraySize = 1000000;

    private Vector4[] _vector4Array;
    private uint[] _uintArray;

    private Vector4 _vectorValue = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);
    private uint _uintValue = 42;

    [GlobalSetup]
    public void Setup()
    {
        _vector4Array = new Vector4[ArraySize];
        _uintArray = new uint[ArraySize];
    }

    [Benchmark(Description = "Vector4 Array - Standard Loop")]
    public void Vector4StandardLoop()
    {
        for (int i = 0; i < _vector4Array.Length; i++)
        {
            _vector4Array[i] = _vectorValue;
        }
    }

    [Benchmark(Description = "Vector4 Array - Unsafe Pointer")]
    public unsafe void Vector4UnsafePointer()
    {
        fixed (Vector4* ptr = _vector4Array)
        {
            for (int i = 0; i < _vector4Array.Length; i++)
            {
                ptr[i] = _vectorValue;
            }
        }
    }

    [Benchmark(Description = "Vector4 Array - SIMD")]
    public unsafe void Vector4SIMD()
    {
        if (Vector256.IsHardwareAccelerated)
        {
            fixed (Vector4* destination = _vector4Array)
            {
                Vector256<float> value = Vector256.Create(
                    _vectorValue.X, _vectorValue.Y, _vectorValue.Z, _vectorValue.W,
                    _vectorValue.X, _vectorValue.Y, _vectorValue.Z, _vectorValue.W);

                float* floatPtr = (float*)destination;

                for (int i = 0; i < _vector4Array.Length; i += 2)
                {
                    Avx.Store(floatPtr, value);
                    floatPtr += 8; // Moving 8 floats ahead (2 Vector4s)
                }
            }
        }
        else
        {
            // Fallback for systems without Vector256 hardware acceleration
            fixed (Vector4* ptr = _vector4Array)
            {
                for (int i = 0; i < _vector4Array.Length; i++)
                {
                    ptr[i] = _vectorValue;
                }
            }
        }
    }

    [Benchmark(Description = "UInt Array - Standard Loop")]
    public void UIntStandardLoop()
    {
        for (int i = 0; i < _uintArray.Length; i++)
        {
            _uintArray[i] = _uintValue;
        }
    }

    [Benchmark(Description = "UInt Array - Unsafe Pointer")]
    public unsafe void UIntUnsafePointer()
    {
        fixed (uint* ptr = _uintArray)
        {
            for (int i = 0; i < _uintArray.Length; i++)
            {
                ptr[i] = _uintValue;
            }
        }
    }

    [Benchmark(Description = "UInt Array - SIMD")]
    public unsafe void UIntSIMD()
    {
        if (Vector256.IsHardwareAccelerated)
        {
            fixed (uint* destination = _uintArray)
            {
                Vector256<uint> value = Vector256.Create(_uintValue);

                for (int i = 0; i < _uintArray.Length; i += 8)
                {
                    Avx2.Store(destination + i, value);
                }
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            fixed (uint* destination = _uintArray)
            {
                Vector128<uint> value = Vector128.Create(_uintValue);

                for (int i = 0; i < _uintArray.Length; i += 4)
                {
                    Sse2.Store(destination + i, value);
                }
            }
        }
        else
        {
            // Fallback for systems without SIMD hardware acceleration
            fixed (uint* ptr = _uintArray)
            {
                for (int i = 0; i < _uintArray.Length; i++)
                {
                    ptr[i] = _uintValue;
                }
            }
        }
    }

    [Benchmark(Description = "UInt Array - Span Fill")]
    public void UIntSpanFill()
    {
        Span<uint> span = _uintArray.AsSpan();
        span.Fill(_uintValue);
    }

    [Benchmark(Description = "Vector4 Array - Span Fill")]
    public void Vector4SpanFill()
    {
        Span<Vector4> span = _vector4Array.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = _vectorValue;
        }
    }
}

