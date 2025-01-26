using BenchmarkDotNet.Attributes;

namespace Alco.Benchmark;

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

    [IterationSetup]
    public void Setup()
    {
        _task = new TestAddTask();
        for (int i = 0; i < _resuableTasks.Length; i++)
        {
            _resuableTasks[i] = new TestAddTask();
        }
    }

    [Benchmark(Description = "Reuseable Task")]
    public void BenchmarkReuseableTask()
    {
        for (int i = 0; i < _count; i++)
        {
            _task.Run();
            //_task.Wait();
        }
    }

    [Benchmark(Description = "System Task")]
    public void BenchmarkSystemTask()
    {
        for (int i = 0; i < _count; i++)
        {
            Task<int> task = Task.Run(() => { return 1; });
            task.Wait();
        }
    }

    [Benchmark(Description = "Reuseable Task WaitAll")]
    public void BenchmarkReuseableTaskArray()
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