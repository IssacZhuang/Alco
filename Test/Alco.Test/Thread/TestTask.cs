using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Alco.Test;

public class TestTask
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
    private class TestErrorTask : ReusableTask
    {
        protected override void ExecuteCore()
        {
            throw new Exception("Test Error");
        }

        public void Run()
        {
            RunCore();
        }
    }

    // Test classes for ReuseableParallelTask
    private class TestParallelCounterTask : ReuseableBatchTask
    {
        private int _counter;
        private readonly object _lock = new object();

        public int Counter => _counter;

        protected override void ExecuteCore(int index)
        {
            lock (_lock)
            {
                _counter++;
            }
        }

        public void ResetCounter()
        {
            lock (_lock)
            {
                _counter = 0;
            }
        }
    }

    private class TestParallelArrayTask : ReuseableBatchTask
    {
        private readonly int[] _array;

        public int[] Array => _array;

        public TestParallelArrayTask(int size)
        {
            _array = new int[size];
        }

        protected override void ExecuteCore(int index)
        {
            _array[index] = index * 2;
        }
    }

    private class TestParallelExceptionTask : ReuseableBatchTask
    {
        private readonly int _errorIndex;

        public TestParallelExceptionTask(int errorIndex)
        {
            _errorIndex = errorIndex;
        }

        protected override void ExecuteCore(int index)
        {
            if (index == _errorIndex)
            {
                throw new InvalidOperationException($"Test exception at index {index}");
            }
            Thread.Sleep(1); // Simulate some work
        }
    }

    private class TestParallelConcurrencyTask : ReuseableBatchTask
    {
        private readonly List<int> _threadIds = new List<int>();
        private readonly object _lock = new object();

        public List<int> ThreadIds => _threadIds;

        public TestParallelConcurrencyTask(int maxConcurrency) : base(maxConcurrency)
        {
        }

        protected override void ExecuteCore(int index)
        {
            lock (_lock)
            {
                _threadIds.Add(Thread.CurrentThread.ManagedThreadId);
            }
            Thread.Sleep(10); // Simulate work
        }
    }

    [Test(Description = "Test Task")]
    public void TestReuseableTask()
    {
        TestAddTask task = new TestAddTask();
        task.Run();
        Assert.That(task.Result, Is.EqualTo(1));
        task.Run();
        Assert.That(task.Result, Is.EqualTo(2));
        task.Run();
        Assert.That(task.Result, Is.EqualTo(3));

        for (int i = 0; i < 10000; i++)
        {
            task.Run();
            task.Wait();
        }

        Assert.That(task.Result, Is.EqualTo(10003));

        for (int i = 0; i < 10000; i++)
        {
            task.Run();
        }

        Assert.That(task.Result, Is.EqualTo(20003));

        for (int i = 0; i < 10000; i++)
        {
            task.Wait();
        }

        Assert.That(task.Result, Is.EqualTo(20003));

        TestErrorTask errorTask = new TestErrorTask();
        errorTask.Run();
        Assert.Catch(() => errorTask.Wait());
    }

    [Test(Description = "Test Parallel Task Basic Functionality")]
    public void TestReuseableParallelTaskBasic()
    {
        using var task = new TestParallelCounterTask();

        // Test with small count
        task.RunParallel(10);
        Assert.That(task.Counter, Is.EqualTo(10));

        // Test reusability
        task.ResetCounter();
        task.RunParallel(5);
        Assert.That(task.Counter, Is.EqualTo(5));

        // Test with zero count
        task.ResetCounter();
        task.RunParallel(0);
        Assert.That(task.Counter, Is.EqualTo(0));

        // Test with negative count
        task.ResetCounter();
        task.RunParallel(-5);
        Assert.That(task.Counter, Is.EqualTo(0));
    }

    [Test(Description = "Test Parallel Task Array Processing")]
    public void TestReuseableParallelTaskArray()
    {
        const int arraySize = 100;
        using var task = new TestParallelArrayTask(arraySize);

        task.RunParallel(arraySize);

        // Verify all elements were processed correctly
        for (int i = 0; i < arraySize; i++)
        {
            Assert.That(task.Array[i], Is.EqualTo(i * 2), $"Array element at index {i} was not processed correctly");
        }
    }

    [Test(Description = "Test Parallel Task with Different Batch Sizes")]
    public void TestReuseableParallelTaskBatchSizes()
    {
        const int totalCount = 50;
        using var task = new TestParallelCounterTask();

        // Test with specific batch size
        task.ResetCounter();
        task.RunParallel(totalCount, batchSize: 5);
        Assert.That(task.Counter, Is.EqualTo(totalCount));

        // Test with batch size of 1
        task.ResetCounter();
        task.RunParallel(totalCount, batchSize: 1);
        Assert.That(task.Counter, Is.EqualTo(totalCount));

        // Test with large batch size
        task.ResetCounter();
        task.RunParallel(totalCount, batchSize: 100);
        Assert.That(task.Counter, Is.EqualTo(totalCount));
    }

    [Test(Description = "Test Parallel Task Exception Handling")]
    public void TestReuseableParallelTaskExceptions()
    {
        using var task = new TestParallelExceptionTask(5);

        // Test that AggregateException is thrown when an exception occurs
        var aggregateException = Assert.Catch<AggregateException>(() => task.RunParallel(10));
        Assert.That(aggregateException.InnerExceptions.Count, Is.EqualTo(1));
        Assert.That(aggregateException.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(aggregateException.InnerExceptions[0].Message, Does.Contain("Test exception at index 5"));
    }

    [Test(Description = "Test Parallel Task Disposal")]
    public void TestReuseableParallelTaskDisposal()
    {
        var task = new TestParallelCounterTask();

        // Use the task
        task.RunParallel(10);
        Assert.That(task.Counter, Is.EqualTo(10));

        // Dispose and verify no exceptions
        Assert.DoesNotThrow(() => task.Dispose());

        // Multiple disposals should not throw
        Assert.DoesNotThrow(() => task.Dispose());
    }
}
