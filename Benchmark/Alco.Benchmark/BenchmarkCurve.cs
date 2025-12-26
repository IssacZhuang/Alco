using BenchmarkDotNet.Attributes;
using System;
using Alco;

namespace Alco.Benchmark;

/// <summary>
/// Benchmark tests for CurveLinear, CurveHermite, and CurveCache Evaluate performance.
/// </summary>
[MemoryDiagnoser]
public class BenchmarkCurve
{
    /// <summary>
    /// The number of nodes in the curve.
    /// </summary>
    [Params(10, 50, 100, 500, 1000, 10000)]
    public int Count;

    private CurveLinear _linear;
    private CurveHermite _hermite;
    private CurveCache _cache;
    private float[] _testTimes;

    /// <summary>
    /// Setup the curves and test times.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        var points = new CurvePoint<float>[Count];
        for (int i = 0; i < Count; i++)
        {
            // Spread points over a reasonable time range [0, Count - 1]
            // Using a simple sine wave for values
            points[i] = new CurvePoint<float>(i, (float)Math.Sin(i * 0.1));
        }

        _linear = new CurveLinear(points);
        _hermite = new CurveHermite(points);
        _cache = new CurveCache(_linear);

        // Pre-generate random times for evaluation to avoid Random overhead during benchmark
        _testTimes = new float[1024];
        var rand = new Random(42);
        float maxTime = Count - 1;
        for (int i = 0; i < _testTimes.Length; i++)
        {
            _testTimes[i] = (float)rand.NextDouble() * maxTime;
        }
    }

    /// <summary>
    /// Benchmark for CurveLinear.Evaluate.
    /// </summary>
    /// <returns>Accumulated result to prevent dead code elimination.</returns>
    [Benchmark(Description = "CurveLinear.Evaluate")]
    public float CurveLinear_Evaluate()
    {
        float result = 0;
        // Evaluate multiple times to reduce overhead of loop and array access
        for (int i = 0; i < 16; i++)
        {
            result += _linear.Evaluate(_testTimes[i & 1023]);
        }
        return result;
    }

    /// <summary>
    /// Benchmark for CurveHermite.Evaluate.
    /// </summary>
    /// <returns>Accumulated result to prevent dead code elimination.</returns>
    [Benchmark(Description = "CurveHermite.Evaluate")]
    public float CurveHermite_Evaluate()
    {
        float result = 0;
        for (int i = 0; i < 16; i++)
        {
            result += _hermite.Evaluate(_testTimes[i & 1023]);
        }
        return result;
    }

    /// <summary>
    /// Benchmark for CurveCache.Evaluate.
    /// </summary>
    /// <returns>Accumulated result to prevent dead code elimination.</returns>
    [Benchmark(Description = "CurveCache.Evaluate")]
    public float CurveCache_Evaluate()
    {
        float result = 0;
        for (int i = 0; i < 16; i++)
        {
            result += _cache.Evaluate(_testTimes[i & 1023]);
        }
        return result;
    }
}

