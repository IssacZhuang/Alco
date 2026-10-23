using BenchmarkDotNet.Attributes;
using System;
using System.Numerics;
using Alco;

namespace Alco.Benchmark;

/// <summary>
/// Compares System.Random and FastRandom for integer, float, vector, and byte generation, including per-call instance creation overhead.
/// </summary>
public class BenchmarkRandom
{
    private const int IterationCount = 10000;
    private const int MaxValue = 100;

    private Random _systemRandom;
    private FastRandom _fastRandom;
    private uint _seed;

    /// <summary>
    /// Creates the random generators used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _systemRandom = new Random(42);
        _fastRandom = new FastRandom(42u);
        _seed = 42u;
    }

    // ==================== NextInt ====================

    /// <summary>
    /// Benchmarks NextInt calls on the shared System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random.Shared - NextInt")]
    public int SystemRandomShared_NextInt()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += Random.Shared.Next();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextInt calls on a local System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random instance - NextInt")]
    public int SystemRandomInstance_NextInt()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _systemRandom.Next();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextInt calls on a local FastRandom instance.
    /// </summary>
    [Benchmark(Description = "FastRandom instance - NextInt")]
    public int FastRandomInstance_NextInt()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _fastRandom.NextInt();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextInt calls on a FastRandom created for each call.
    /// </summary>
    [Benchmark(Description = "FastRandom create on use - NextInt")]
    public int FastRandomCreate_NextInt()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            var rng = new FastRandom(_seed++);
            sum += rng.NextInt();
        }
        return sum;
    }

    // ==================== NextInt(max) ====================

    /// <summary>
    /// Benchmarks bounded Next calls on the shared System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random.Shared - Next(max)")]
    public int SystemRandomShared_NextMax()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += Random.Shared.Next(MaxValue);
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks bounded Next calls on a local System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random instance - Next(max)")]
    public int SystemRandomInstance_NextMax()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _systemRandom.Next(MaxValue);
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks bounded NextInt calls on a local FastRandom instance.
    /// </summary>
    [Benchmark(Description = "FastRandom instance - NextInt(max)")]
    public int FastRandomInstance_NextMax()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _fastRandom.NextInt(MaxValue);
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks bounded NextInt calls on a FastRandom created for each call.
    /// </summary>
    [Benchmark(Description = "FastRandom create on use - NextInt(max)")]
    public int FastRandomCreate_NextMax()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            var rng = new FastRandom(_seed++);
            sum += rng.NextInt(MaxValue);
        }
        return sum;
    }

    // ==================== NextFloat ====================

    /// <summary>
    /// Benchmarks NextSingle calls on the shared System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random.Shared - NextSingle")]
    public float SystemRandomShared_NextSingle()
    {
        float sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += Random.Shared.NextSingle();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextSingle calls on a local System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random instance - NextSingle")]
    public float SystemRandomInstance_NextSingle()
    {
        float sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _systemRandom.NextSingle();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextFloat calls on a local FastRandom instance.
    /// </summary>
    [Benchmark(Description = "FastRandom instance - NextFloat")]
    public float FastRandomInstance_NextFloat()
    {
        float sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _fastRandom.NextFloat();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextFloat calls on a FastRandom created for each call.
    /// </summary>
    [Benchmark(Description = "FastRandom create on use - NextFloat")]
    public float FastRandomCreate_NextFloat()
    {
        float sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            var rng = new FastRandom(_seed++);
            sum += rng.NextFloat();
        }
        return sum;
    }

    // ==================== NextVector2 ====================

    /// <summary>
    /// Benchmarks Vector2 generation using the shared System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random.Shared - Vector2")]
    public Vector2 SystemRandomShared_Vector2()
    {
        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += new Vector2(Random.Shared.NextSingle(), Random.Shared.NextSingle());
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks Vector2 generation using a local System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random instance - Vector2")]
    public Vector2 SystemRandomInstance_Vector2()
    {
        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += new Vector2(_systemRandom.NextSingle(), _systemRandom.NextSingle());
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextVector2 calls on a local FastRandom instance.
    /// </summary>
    [Benchmark(Description = "FastRandom instance - NextVector2")]
    public Vector2 FastRandomInstance_NextVector2()
    {
        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _fastRandom.NextVector2();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextVector2 calls on a FastRandom created for each call.
    /// </summary>
    [Benchmark(Description = "FastRandom create on use - NextVector2")]
    public Vector2 FastRandomCreate_NextVector2()
    {
        Vector2 sum = Vector2.Zero;
        for (int i = 0; i < IterationCount; i++)
        {
            var rng = new FastRandom(_seed++);
            sum += rng.NextVector2();
        }
        return sum;
    }

    // ==================== NextByte ====================

    /// <summary>
    /// Benchmarks single-byte NextBytes calls on the shared System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random.Shared - NextBytes")]
    public int SystemRandomShared_NextBytes()
    {
        byte[] buffer = new byte[1];
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            Random.Shared.NextBytes(buffer);
            sum += buffer[0];
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks single-byte NextBytes calls on a local System.Random instance.
    /// </summary>
    [Benchmark(Description = "System.Random instance - NextBytes")]
    public int SystemRandomInstance_NextBytes()
    {
        byte[] buffer = new byte[1];
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            _systemRandom.NextBytes(buffer);
            sum += buffer[0];
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextByte calls on a local FastRandom instance.
    /// </summary>
    [Benchmark(Description = "FastRandom instance - NextByte")]
    public int FastRandomInstance_NextByte()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            sum += _fastRandom.NextByte();
        }
        return sum;
    }

    /// <summary>
    /// Benchmarks NextByte calls on a FastRandom created for each call.
    /// </summary>
    [Benchmark(Description = "FastRandom create on use - NextByte")]
    public int FastRandomCreate_NextByte()
    {
        int sum = 0;
        for (int i = 0; i < IterationCount; i++)
        {
            var rng = new FastRandom(_seed++);
            sum += rng.NextByte();
        }
        return sum;
    }
}

