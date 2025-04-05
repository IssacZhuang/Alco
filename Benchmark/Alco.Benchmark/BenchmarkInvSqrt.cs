using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkFramework;

namespace Alco.Benchmark;

[CustomConfigParam(12, 50, 1024)]
public class BenchmarkInvSqrt
{

    [Params(1.5f, 10.5f, 100.5f, 1000.5f)]
    public float InputValue { get; set; }

    [Benchmark]
    public float Test_MathF_rsqrt()
    {
        //return value to avoid compiler optimization
        return MathF_rsqrt(InputValue);
    }

    [Benchmark]
    public float Test_MathF_rsqrt2()
    {
        //return value to avoid compiler optimization
        return MathF.ReciprocalSqrtEstimate(InputValue);
    }

    [Benchmark]
    public float Test_Quake3_rsqrt()
    {
        //return value to avoid compiler optimization
        return Quake3_rsqrt(InputValue);
    }

    [Benchmark]
    public float Test_Sse_rsqrt()
    {
        //return value to avoid compiler optimization
        return Sse_rsqrt(InputValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float MathF_rsqrt(float a)
    {
        return 1.0f / MathF.Sqrt(a);
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float Sse_rsqrt(float a)
    {
        Vector128<float> sVec = Vector128.Create(a);
        Vector128<float> invSvec = Sse.ReciprocalSqrt(sVec);
        return invSvec.GetElement(0);
    }
}
