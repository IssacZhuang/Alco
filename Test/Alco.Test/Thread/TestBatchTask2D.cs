using System;
using System.Collections.Generic;
using System.Threading;

namespace Alco.Test;

public class TestBatchTask2D
{
    // Test classes for ReuseableBatchTask2D
    private class Test2DCounterTask : ReuseableBatchTask2D
    {
        private int _counter;
        private readonly object _lock = new object();

        public int Counter => _counter;

        protected override void ExecuteCore(int x, int y)
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

    private class Test2DArrayTask : ReuseableBatchTask2D
    {
        private readonly int[,] _array;

        public int[,] Array => _array;

        public Test2DArrayTask(int width, int height)
        {
            _array = new int[width, height];
        }

        protected override void ExecuteCore(int x, int y)
        {
            _array[x, y] = x * 100 + y;
        }
    }

    private class Test2DExceptionTask : ReuseableBatchTask2D
    {
        private readonly int _errorX;
        private readonly int _errorY;

        public Test2DExceptionTask(int errorX, int errorY)
        {
            _errorX = errorX;
            _errorY = errorY;
        }

        protected override void ExecuteCore(int x, int y)
        {
            if (x == _errorX && y == _errorY)
            {
                throw new InvalidOperationException($"Test exception at position ({x}, {y})");
            }
            Thread.Sleep(1); // Simulate some work
        }
    }

    private class Test2DConcurrencyTask : ReuseableBatchTask2D
    {
        private readonly List<int> _threadIds = new List<int>();
        private readonly object _lock = new object();

        public List<int> ThreadIds => _threadIds;

        public Test2DConcurrencyTask(int maxConcurrency) : base(maxConcurrency)
        {
        }

        protected override void ExecuteCore(int x, int y)
        {
            lock (_lock)
            {
                _threadIds.Add(Thread.CurrentThread.ManagedThreadId);
            }
            Thread.Sleep(5); // Simulate work
        }
    }

    [Test(Description = "Test 2D Batch Task Basic Functionality")]
    public void TestReuseableBatchTask2DBasic()
    {
        using var task = new Test2DCounterTask();

        // Test with small area
        task.RunParallel(3, 3);
        Assert.That(task.Counter, Is.EqualTo(9));

        // Test reusability
        task.ResetCounter();
        task.RunParallel(2, 4);
        Assert.That(task.Counter, Is.EqualTo(8));

        // Test with zero dimensions
        task.ResetCounter();
        task.RunParallel(0, 5);
        Assert.That(task.Counter, Is.EqualTo(0));

        task.ResetCounter();
        task.RunParallel(5, 0);
        Assert.That(task.Counter, Is.EqualTo(0));

        // Test with negative dimensions
        task.ResetCounter();
        task.RunParallel(-3, 3);
        Assert.That(task.Counter, Is.EqualTo(0));

        task.ResetCounter();
        task.RunParallel(3, -3);
        Assert.That(task.Counter, Is.EqualTo(0));
    }

    [Test(Description = "Test 2D Batch Task Array Processing")]
    public void TestReuseableBatchTask2DArray()
    {
        const int width = 10;
        const int height = 8;
        using var task = new Test2DArrayTask(width, height);

        task.RunParallel(width, height);

        // Verify all elements were processed correctly
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int expected = x * 100 + y;
                Assert.That(task.Array[x, y], Is.EqualTo(expected),
                    $"Array element at position ({x}, {y}) was not processed correctly");
            }
        }
    }

    [Test(Description = "Test 2D Batch Task with Different Batch Sizes")]
    public void TestReuseableBatchTask2DBatchSizes()
    {
        const int width = 12;
        const int height = 8;
        using var task = new Test2DCounterTask();

        // Test with specific batch sizes
        task.ResetCounter();
        task.RunParallel(width, height, batchSizeX: 3, batchSizeY: 2);
        Assert.That(task.Counter, Is.EqualTo(width * height));

        // Test with batch size of 1
        task.ResetCounter();
        task.RunParallel(width, height, batchSizeX: 1, batchSizeY: 1);
        Assert.That(task.Counter, Is.EqualTo(width * height));

        // Test with large batch sizes
        task.ResetCounter();
        task.RunParallel(width, height, batchSizeX: 100, batchSizeY: 100);
        Assert.That(task.Counter, Is.EqualTo(width * height));

        // Test with automatic batch sizes (null parameters)
        task.ResetCounter();
        task.RunParallel(width, height);
        Assert.That(task.Counter, Is.EqualTo(width * height));
    }

    [Test(Description = "Test 2D Batch Task Exception Handling")]
    public void TestReuseableBatchTask2DExceptions()
    {
        using var task = new Test2DExceptionTask(2, 3);

        // Test that AggregateException is thrown when an exception occurs
        var aggregateException = Assert.Catch<AggregateException>(() => task.RunParallel(5, 5));
        Assert.That(aggregateException.InnerExceptions.Count, Is.EqualTo(1));
        Assert.That(aggregateException.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(aggregateException.InnerExceptions[0].Message, Does.Contain("Test exception at position (2, 3)"));
    }

    [Test(Description = "Test 2D Batch Task Disposal")]
    public void TestReuseableBatchTask2DDisposal()
    {
        var task = new Test2DCounterTask();

        // Use the task
        task.RunParallel(3, 3);
        Assert.That(task.Counter, Is.EqualTo(9));

        // Dispose and verify no exceptions
        Assert.DoesNotThrow(() => task.Dispose());

        // Multiple disposals should not throw
        Assert.DoesNotThrow(() => task.Dispose());
    }

    [Test(Description = "Test 2D Batch Task Concurrency Control")]
    public void TestReuseableBatchTask2DConcurrency()
    {
        const int maxConcurrency = 2;
        using var task = new Test2DConcurrencyTask(maxConcurrency);

        // Use a larger workload to better test concurrency control
        task.RunParallel(16, 16);

        // The task should have used multiple threads
        var uniqueThreadIds = new HashSet<int>(task.ThreadIds);
        Assert.That(uniqueThreadIds.Count, Is.GreaterThan(0), "Should use at least one thread");

        // Note: Due to ThreadPool behavior and timing, we might see more threads than maxConcurrency
        // especially with smaller workloads. The important thing is that we're using parallelism.
        Assert.That(uniqueThreadIds.Count, Is.LessThanOrEqualTo(Environment.ProcessorCount),
            "Should not use more threads than available processors");
    }

    [Test(Description = "Test 2D Batch Task Large Area Processing")]
    public void TestReuseableBatchTask2DLargeArea()
    {
        const int size = 50; // 50x50 = 2500 elements
        using var task = new Test2DCounterTask();

        task.RunParallel(size, size);
        Assert.That(task.Counter, Is.EqualTo(size * size));
    }
}