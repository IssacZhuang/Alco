using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks reusable tasks against system tasks.
/// </summary>
[MemoryDiagnoser]
public class BenchmarkTask
{
    private class TestAddTask : ReusableTask<int>
    {
        public int value;
        protected override int ExecuteCore()
        {
            return ++value;
        }

        public void Run()
        {
            RunCore();
        }
    }

    private int _count = 10000;
    private TestAddTask _task;

    private TestAddTask[] _resuableTasks = new TestAddTask[10000];
    private Task<int>[] _systemTasks = new Task<int>[10000];

    /// <summary>
    /// Initializes the reusable tasks for each iteration.
    /// </summary>
    [IterationSetup]
    public void Setup()
    {
        _task = new TestAddTask();
        for (int i = 0; i < _resuableTasks.Length; i++)
        {
            _resuableTasks[i] = new TestAddTask();
        }
    }

    /// <summary>
    /// Benchmarks running reusable tasks without waiting.
    /// </summary>
    [Benchmark(Description = "Reusable Task")]
    public void BenchmarkReusableTask()
    {
        for (int i = 0; i < _count; i++)
        {
            _task.Run();
            //_task.Wait();
        }
    }

    /// <summary>
    /// Benchmarks running and waiting for a system task.
    /// </summary>
    [Benchmark(Description = "System Task")]
    public void BenchmarkSystemTask()
    {
        for (int i = 0; i < _count; i++)
        {
            Task<int> task = Task.Run(() => { return 1; });
            task.Wait();
        }
    }

    /// <summary>
    /// Benchmarks running an array of reusable tasks and waiting for all of them.
    /// </summary>
    [Benchmark(Description = "Reusable Task WaitAll")]
    public void BenchmarkReusableTaskArray()
    {
        for (int i = 0; i < _count; i++)
        {
            _resuableTasks[i].Run();
        }

        for (int i = 0; i < _count; i++)
        {
            _resuableTasks[i].Wait();
        }
    }

    /// <summary>
    /// Benchmarks running an array of system tasks and waiting for all of them.
    /// </summary>
    [Benchmark(Description = "System Task WaitAll")]    
    public void BenchmarkSystemTaskArray()
    {
        for (int i = 0; i < _count; i++)
        {
            _systemTasks[i] = Task.Run(() => { return 1; });
        }

        Task.WaitAll(_systemTasks);
    }


}