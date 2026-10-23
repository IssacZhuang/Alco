using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkFramework;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks reciprocal square root implementations.
/// </summary>
[CustomConfigParam(12, 50, 1024)]
public class BenchmarkInvSqrt
{

    /// <summary>
    /// Gets or sets the input value used by each benchmark.
    /// </summary>
    [Params(1.5f, 10.5f, 100.5f, 1000.5f)]
    public float InputValue { get; set; }

    /// <summary>
    /// Benchmarks the reciprocal square root computed from <see cref="MathF.Sqrt"/>.
    /// </summary>
    [Benchmark]
    public float Test_MathF_rsqrt()
    {
        //return value to avoid compiler optimization
        return MathF_rsqrt(InputValue);
    }

    /// <summary>
    /// Benchmarks <see cref="MathF.ReciprocalSqrtEstimate"/>.
    /// </summary>
    [Benchmark]
    public float Test_MathF_rsqrt2()
    {
        //return value to avoid compiler optimization
        return MathF.ReciprocalSqrtEstimate(InputValue);
    }

    /// <summary>
    /// Benchmarks the Quake III reciprocal square root approximation.
    /// </summary>
    [Benchmark]
    public float Test_Quake3_rsqrt()
    {
        //return value to avoid compiler optimization
        return Quake3_rsqrt(InputValue);
    }

    /// <summary>
    /// Benchmarks the SSE reciprocal square root estimate.
    /// </summary>
    [Benchmark]
    public float Test_Sse_rsqrt()
    {
        //return value to avoid compiler optimization
        return Sse_rsqrt(InputValue);
    }

    /// <summary>
    /// Computes the reciprocal square root using <see cref="MathF.Sqrt"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float MathF_rsqrt(float a)
    {
        return 1.0f / MathF.Sqrt(a);
    }

    /// <summary>
    /// Computes the reciprocal square root using the Quake III fast inverse square root.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float Quake3_rsqrt(float a)
    {
        // from q_math.c of Quake III
        float xhalf = 0.5f * a;
        int i = *(int*)&a;
        i = 0x5f3759df - (i >> 1);
        a = *(float*)&i;
        a = a * (1.5f - xhalf * a * a);
        return a;
    }

    /// <summary>
    /// Computes the reciprocal square root using the SSE reciprocal square root estimate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float Sse_rsqrt(float a)
    {
        Vector128<float> sVec = Vector128.Create(a);
        Vector128<float> invSvec = Sse.ReciprocalSqrt(sVec);
        return invSvec.GetElement(0);
    }
}
