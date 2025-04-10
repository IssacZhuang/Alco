using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Alco;

namespace Alco.Benchmark;

public class BenchmarkQueuePush
{
    private const int Count = 1000;
    private ConcurrentQueue<int> _concurrentQueue;
    private CircularWorkStealingDeque<int> _circularWorkStealingDeque;

    [GlobalSetup]
    public void Setup()
    {
        _concurrentQueue = new ConcurrentQueue<int>();
        _circularWorkStealingDeque = new CircularWorkStealingDeque<int>(1024);
    }

    [Benchmark(Description = "ConcurrentQueue push")]
    public void ConcurrentQueue()
    {
        for (int i = 0; i < Count; i++)
        {
            _concurrentQueue.Enqueue(i);
        }
    }

    [Benchmark(Description = "CircularWorkStealingDeque push")]
    public void CircularWorkStealingDeque()
    {
        for (int i = 0; i < Count; i++)
        {
            _circularWorkStealingDeque.Push(i);
        }
    }
}

public class BenchmarkQueuePop{
    private const int Count = 1000;
    private ConcurrentQueue<int> _concurrentQueue;
    private CircularWorkStealingDeque<int> _circularWorkStealingDeque;

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

    [Benchmark(Description = "ConcurrentQueue pop single thread")]
    public void ConcurrentQueue()
    {
        for (int i = 0; i < Count; i++)
        {
            _concurrentQueue.TryDequeue(out _);
        }
    }

    [Benchmark(Description = "CircularWorkStealingDeque pop single thread")]
    public void CircularWorkStealingDeque()
    {
        for (int i = 0; i < Count; i++)
        {
            _circularWorkStealingDeque.TrySteal(out _);
        }
    }

    [Benchmark(Description = "ConcurrentQueue pop multi thread")]
    public void ConcurrentQueueMultiThread()
    {
        Parallel.For(0, Count, (i) =>
        {
            _concurrentQueue.TryDequeue(out _);
        });
    }

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