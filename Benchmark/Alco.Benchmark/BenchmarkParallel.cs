using BenchmarkDotNet.Attributes;
using System;
using System.Threading;
using System.Threading.Tasks;
using Alco;

namespace Alco.Benchmark;

/// <summary>
/// Benchmark comparing Parallel.For, ParallelScheduler, and ReuseableParallelTask performance
/// across different workload types and scales.
/// </summary>
[MemoryDiagnoser]
public class BenchmarkParallel
{
    private const int SmallWorkload = 1000;
    private const int MediumWorkload = 10000;
    private const int LargeWorkload = 100000;

    private CpuIntensiveTask _cpuTask;
    private MemoryIntensiveTask _memoryTask;
    private LightweightTask _lightweightTask;

    private int[] _array;
    private Random _random;

    [GlobalSetup]
    public void Setup()
    {
        _cpuTask = new CpuIntensiveTask();
        _memoryTask = new MemoryIntensiveTask();
        _lightweightTask = new LightweightTask();
        _array = new int[LargeWorkload];
        _random = new Random(42);

        // Initialize array with random values
        for (int i = 0; i < _array.Length; i++)
        {
            _array[i] = (int)_random.NextUint(1000);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cpuTask?.Dispose();
        _memoryTask?.Dispose();
        _lightweightTask?.Dispose();
        _emptyTask?.Dispose();
    }

    #region CPU Intensive Tasks

    /// <summary>
    /// Reusable parallel task for CPU-intensive work (calculating square roots and trigonometric functions)
    /// </summary>
    private class CpuIntensiveTask : ReuseableBatchTask
    {
        private int[] _data;
        private double[] _results;

        /// <summary>
        /// Sets the input data and results array for the CPU-intensive task.
        /// </summary>
        /// <param name="data">The input data array.</param>
        /// <param name="results">The results array to store computed values.</param>
        public void SetData(int[] data, double[] results)
        {
            _data = data;
            _results = results;
        }

        protected override void ExecuteCore(int index)
        {
            // CPU-intensive calculation: multiple mathematical operations
            double value = _data[index];
            for (int i = 0; i < 10; i++)
            {
                value = Math.Sqrt(value * Math.Sin(value) + Math.Cos(value));
            }
            _results[index] = value;
        }
    }

    [Benchmark(Description = "CPU Intensive - Parallel.For")]
    [Arguments(SmallWorkload)]
    [Arguments(MediumWorkload)]
    [Arguments(LargeWorkload)]
    public double[] CpuIntensive_ParallelFor(int size)
    {
        var results = new double[size];
        Parallel.For(0, size, i =>
        {
            double value = _array[i];
            for (int j = 0; j < 10; j++)
            {
                value = Math.Sqrt(value * Math.Sin(value) + Math.Cos(value));
            }
            results[i] = value;
        });
        return results;
    }

    [Benchmark(Description = "CPU Intensive - ReuseableParallelTask")]
    [Arguments(SmallWorkload)]
    [Arguments(MediumWorkload)]
    [Arguments(LargeWorkload)]
    public double[] CpuIntensive_ReuseableParallelTask(int size)
    {
        var results = new double[size];
        _cpuTask.SetData(_array, results);
        _cpuTask.RunParallel(size);
        return results;
    }

    #endregion

    #region Memory Intensive Tasks

    /// <summary>
    /// Reusable parallel task for memory-intensive work (array operations and memory access patterns)
    /// </summary>
    private class MemoryIntensiveTask : ReuseableBatchTask
    {
        private int[] _source;
        private int[] _destination;

        /// <summary>
        /// Sets the source and destination arrays for the memory-intensive task.
        /// </summary>
        /// <param name="source">The source array to read from.</param>
        /// <param name="destination">The destination array to write results to.</param>
        public void SetData(int[] source, int[] destination)
        {
            _source = source;
            _destination = destination;
        }

        protected override void ExecuteCore(int index)
        {
            // Memory-intensive work: multiple array accesses with random patterns
            int sum = 0;
            for (int i = 0; i < 20; i++)
            {
                int idx = (index * 31 + i * 17) % _source.Length;
                sum += _source[idx];
            }
            _destination[index] = sum;
        }
    }

    [Benchmark(Description = "Memory Intensive - Parallel.For")]
    [Arguments(SmallWorkload)]
    [Arguments(MediumWorkload)]
    [Arguments(LargeWorkload)]
    public int[] MemoryIntensive_ParallelFor(int size)
    {
        var results = new int[size];
        Parallel.For(0, size, i =>
        {
            int sum = 0;
            for (int j = 0; j < 20; j++)
            {
                int idx = (i * 31 + j * 17) % _array.Length;
                sum += _array[idx];
            }
            results[i] = sum;
        });
        return results;
    }


    [Benchmark(Description = "Memory Intensive - ReuseableParallelTask")]
    [Arguments(SmallWorkload)]
    [Arguments(MediumWorkload)]
    [Arguments(LargeWorkload)]
    public int[] MemoryIntensive_ReuseableParallelTask(int size)
    {
        var results = new int[size];
        _memoryTask.SetData(_array, results);
        _memoryTask.RunParallel(size);
        return results;
    }

    #endregion

    #region Lightweight Tasks

    /// <summary>
    /// Reusable parallel task for lightweight work (simple arithmetic operations)
    /// </summary>
    private class LightweightTask : ReuseableBatchTask
    {
        private int[] _source;
        private int[] _destination;

        /// <summary>
        /// Sets the source and destination arrays for the lightweight task.
        /// </summary>
        /// <param name="source">The source array to read from.</param>
        /// <param name="destination">The destination array to write results to.</param>
        public void SetData(int[] source, int[] destination)
        {
            _source = source;
            _destination = destination;
        }

        protected override void ExecuteCore(int index)
        {
            // Lightweight work: simple arithmetic
            _destination[index] = _source[index] * 2 + 1;
        }
    }

    [Benchmark(Description = "Lightweight - Parallel.For")]
    [Arguments(SmallWorkload)]
    [Arguments(MediumWorkload)]
    [Arguments(LargeWorkload)]
    public int[] Lightweight_ParallelFor(int size)
    {
        var results = new int[size];
        Parallel.For(0, size, i =>
        {
            results[i] = _array[i] * 2 + 1;
        });
        return results;
    }


    [Benchmark(Description = "Lightweight - ReuseableParallelTask")]
    [Arguments(SmallWorkload)]
    [Arguments(MediumWorkload)]
    [Arguments(LargeWorkload)]
    public int[] Lightweight_ReuseableParallelTask(int size)
    {
        var results = new int[size];
        _lightweightTask.SetData(_array, results);
        _lightweightTask.RunParallel(size);
        return results;
    }

    #endregion

    #region Scheduling Performance Tests

    /// <summary>
    /// Reusable parallel task for empty work (no operations) to test pure scheduling overhead
    /// </summary>
    private class EmptyTask : ReuseableBatchTask
    {
        protected override void ExecuteCore(int index)
        {
            // Completely empty - pure scheduling overhead test
        }
    }

    private readonly EmptyTask _emptyTask = new EmptyTask();

    private const int SchedulingIterations = 1000;

    /// <summary>
    /// Test scheduling performance with empty tasks using Parallel.For repeated execution
    /// </summary>
    [Benchmark(Description = "Scheduling Performance - Parallel.For (Repeated)")]
    [Arguments(100)]
    public void SchedulingPerformance_ParallelFor_Repeated(int taskCount)
    {
        for (int iteration = 0; iteration < SchedulingIterations; iteration++)
        {
            Parallel.For(0, taskCount, i =>
            {
                // Empty task - pure scheduling overhead
            });
        }
    }

    /// <summary>
    /// Test scheduling performance with empty tasks using ReuseableParallelTask repeated execution
    /// </summary>
    [Benchmark(Description = "Scheduling Performance - ReuseableParallelTask (Repeated)")]
    [Arguments(100)]
    public void SchedulingPerformance_ReuseableParallelTask_Repeated(int taskCount)
    {
        for (int iteration = 0; iteration < SchedulingIterations; iteration++)
        {
            _emptyTask.RunParallel(taskCount);
        }
    }

   
    #endregion

    #region Overhead Tests

    /// <summary>
    /// Test the overhead of each parallel execution method with minimal work
    /// </summary>

    [Benchmark(Description = "Overhead - Parallel.For")]
    public int Overhead_ParallelFor()
    {
        int count = 0;
        Parallel.For(0, 100, i =>
        {
            Interlocked.Increment(ref count);
        });
        return count;
    }

    private class OverheadTask : ReuseableBatchTask
    {
        private int _count;

        /// <summary>
        /// Gets the current count of executed tasks.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Resets the task count to zero.
        /// </summary>
        public void Reset()
        {
            _count = 0;
        }

        protected override void ExecuteCore(int index)
        {
            Interlocked.Increment(ref _count);
        }
    }

    private readonly OverheadTask _overheadTask = new OverheadTask();

    [Benchmark(Description = "Overhead - ReuseableParallelTask")]
    public int Overhead_ReuseableParallelTask()
    {
        _overheadTask.Reset();
        _overheadTask.RunParallel(100);
        return _overheadTask.Count;
    }

    #endregion
}