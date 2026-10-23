using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Alco;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks pushing 1000 items to a ConcurrentQueue and a CircularWorkStealingDeque.
/// </summary>
public class BenchmarkQueuePush
{
    private const int Count = 1000;
    private ConcurrentQueue<int> _concurrentQueue;
    private CircularWorkStealingDeque<int> _circularWorkStealingDeque;

    /// <summary>
    /// Creates the queues used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _concurrentQueue = new ConcurrentQueue<int>();
        _circularWorkStealingDeque = new CircularWorkStealingDeque<int>(1024);
    }

    /// <summary>
    /// Benchmarks pushing 1000 items to a ConcurrentQueue.
    /// </summary>
    [Benchmark(Description = "ConcurrentQueue push")]
    public void ConcurrentQueue()
    {
        for (int i = 0; i < Count; i++)
        {
            _concurrentQueue.Enqueue(i);
        }
    }

    /// <summary>
    /// Benchmarks pushing 1000 items to a CircularWorkStealingDeque.
    /// </summary>
    [Benchmark(Description = "CircularWorkStealingDeque push")]
    public void CircularWorkStealingDeque()
    {
        for (int i = 0; i < Count; i++)
        {
            _circularWorkStealingDeque.Push(i);
        }
    }
}

/// <summary>
/// Benchmarks popping 1000 items from a ConcurrentQueue and a CircularWorkStealingDeque on one and multiple threads.
/// </summary>
public class BenchmarkQueuePop{
    private const int Count = 1000;
    private ConcurrentQueue<int> _concurrentQueue;
    private CircularWorkStealingDeque<int> _circularWorkStealingDeque;

    /// <summary>
    /// Creates and fills the queues used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _concurrentQueue = new ConcurrentQueue<int>();
        _circularWorkStealingDeque = new CircularWorkStealingDeque<int>(1024);
        for (int i = 0; i < Count; i++)
        {
            _concurrentQueue.Enqueue(i);
            _circularWorkStealingDeque.Push(i);
        }
    }

    /// <summary>
    /// Benchmarks popping 1000 items from a ConcurrentQueue on a single thread.
    /// </summary>
    [Benchmark(Description = "ConcurrentQueue pop single thread")]
    public void ConcurrentQueue()
    {
        for (int i = 0; i < Count; i++)
        {
            _concurrentQueue.TryDequeue(out _);
        }
    }

    /// <summary>
    /// Benchmarks stealing 1000 items from a CircularWorkStealingDeque on a single thread.
    /// </summary>
    [Benchmark(Description = "CircularWorkStealingDeque pop single thread")]
    public void CircularWorkStealingDeque()
    {
        for (int i = 0; i < Count; i++)
        {
            _circularWorkStealingDeque.TrySteal(out _);
        }
    }

    /// <summary>
    /// Benchmarks popping 1000 items from a ConcurrentQueue in parallel.
    /// </summary>
    [Benchmark(Description = "ConcurrentQueue pop multi thread")]
    public void ConcurrentQueueMultiThread()
    {
        Parallel.For(0, Count, (i) =>
        {
            _concurrentQueue.TryDequeue(out _);
        });
    }

    /// <summary>
    /// Benchmarks stealing all items from a CircularWorkStealingDeque in parallel.
    /// </summary>
    [Benchmark(Description = "CircularWorkStealingDeque pop multi thread")]
    public void CircularWorkStealingDequeMultiThread()
    {
        Parallel.For(0, Count, (i) =>
        {
            while (true)
            {
                StealingResult result = _circularWorkStealingDeque.TrySteal(out _);
                if (result == StealingResult.Empty)
                {
                    break;
                }
            }
        });
    }
}