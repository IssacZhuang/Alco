using BenchmarkDotNet.Attributes;

namespace Vocore.Benchmark;

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
    }

    private int _count = 10000;
    private TestAddTask _task;

    [IterationSetup]
    public void Setup()
    {
        _task = new TestAddTask();
    }

    [Benchmark(Description = "Reuseable Task")]
    public void BenchmarkReuseableTask()
    {
        for (int i = 0; i < _count; i++)
        {
            _task.Run();
            _task.Wait();
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
}