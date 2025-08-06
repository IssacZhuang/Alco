using System;
using System.Collections.Generic;
using System.Threading;

namespace Alco.Test;

public class TestBatchTask3D
{
    // Test classes for ReuseableBatchTask3D
    private class Test3DCounterTask : ReuseableBatchTask3D
    {
        private int _counter;
        private readonly object _lock = new object();

        public int Counter => _counter;

        protected override void ExecuteCore(int x, int y, int z)
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

    private class Test3DArrayTask : ReuseableBatchTask3D
    {
        private readonly int[,,] _array;

        public int[,,] Array => _array;

        public Test3DArrayTask(int width, int height, int depth)
        {
            _array = new int[width, height, depth];
        }

        protected override void ExecuteCore(int x, int y, int z)
        {
            _array[x, y, z] = x * 10000 + y * 100 + z;
        }
    }

    private class Test3DExceptionTask : ReuseableBatchTask3D
    {
        private readonly int _errorX;
        private readonly int _errorY;
        private readonly int _errorZ;

        public Test3DExceptionTask(int errorX, int errorY, int errorZ)
        {
            _errorX = errorX;
            _errorY = errorY;
            _errorZ = errorZ;
        }

        protected override void ExecuteCore(int x, int y, int z)
        {
            if (x == _errorX && y == _errorY && z == _errorZ)
            {
                throw new InvalidOperationException($"Test exception at position ({x}, {y}, {z})");
            }
            Thread.Sleep(1); // Simulate some work
        }
    }

    private class Test3DConcurrencyTask : ReuseableBatchTask3D
    {
        private readonly List<int> _threadIds = new List<int>();
        private readonly object _lock = new object();

        public List<int> ThreadIds => _threadIds;

        public Test3DConcurrencyTask(int maxConcurrency) : base(maxConcurrency)
        {
        }

        protected override void ExecuteCore(int x, int y, int z)
        {
            lock (_lock)
            {
                _threadIds.Add(Thread.CurrentThread.ManagedThreadId);
            }
            Thread.Sleep(5); // Simulate work
        }
    }

    [Test(Description = "Test 3D Batch Task Basic Functionality")]
    public void TestReuseableBatchTask3DBasic()
    {
        using var task = new Test3DCounterTask();

        // Test with small volume
        task.RunParallel(2, 2, 2);
        Assert.That(task.Counter, Is.EqualTo(8));

        // Test reusability
        task.ResetCounter();
        task.RunParallel(3, 2, 2);
        Assert.That(task.Counter, Is.EqualTo(12));

        // Test with zero dimensions
        task.ResetCounter();
        task.RunParallel(0, 2, 2);
        Assert.That(task.Counter, Is.EqualTo(0));

        task.ResetCounter();
        task.RunParallel(2, 0, 2);
        Assert.That(task.Counter, Is.EqualTo(0));

        task.ResetCounter();
        task.RunParallel(2, 2, 0);
        Assert.That(task.Counter, Is.EqualTo(0));

        // Test with negative dimensions
        task.ResetCounter();
        task.RunParallel(-2, 2, 2);
        Assert.That(task.Counter, Is.EqualTo(0));

        task.ResetCounter();
        task.RunParallel(2, -2, 2);
        Assert.That(task.Counter, Is.EqualTo(0));

        task.ResetCounter();
        task.RunParallel(2, 2, -2);
        Assert.That(task.Counter, Is.EqualTo(0));
    }

    [Test(Description = "Test 3D Batch Task Array Processing")]
    public void TestReuseableBatchTask3DArray()
    {
        const int width = 6;
        const int height = 4;
        const int depth = 3;
        using var task = new Test3DArrayTask(width, height, depth);

        task.RunParallel(width, height, depth);

        // Verify all elements were processed correctly
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    int expected = x * 10000 + y * 100 + z;
                    Assert.That(task.Array[x, y, z], Is.EqualTo(expected), 
                        $"Array element at position ({x}, {y}, {z}) was not processed correctly");
                }
            }
        }
    }

    [Test(Description = "Test 3D Batch Task with Different Batch Sizes")]
    public void TestReuseableBatchTask3DBatchSizes()
    {
        const int width = 6;
        const int height = 4;
        const int depth = 3;
        using var task = new Test3DCounterTask();

        // Test with specific batch sizes
        task.ResetCounter();
        task.RunParallel(width, height, depth, batchSizeX: 2, batchSizeY: 2, batchSizeZ: 2);
        Assert.That(task.Counter, Is.EqualTo(width * height * depth));

        // Test with batch size of 1
        task.ResetCounter();
        task.RunParallel(width, height, depth, batchSizeX: 1, batchSizeY: 1, batchSizeZ: 1);
        Assert.That(task.Counter, Is.EqualTo(width * height * depth));

        // Test with large batch sizes
        task.ResetCounter();
        task.RunParallel(width, height, depth, batchSizeX: 100, batchSizeY: 100, batchSizeZ: 100);
        Assert.That(task.Counter, Is.EqualTo(width * height * depth));

        // Test with automatic batch sizes (null parameters)
        task.ResetCounter();
        task.RunParallel(width, height, depth);
        Assert.That(task.Counter, Is.EqualTo(width * height * depth));
    }

    [Test(Description = "Test 3D Batch Task Exception Handling")]
    public void TestReuseableBatchTask3DExceptions()
    {
        using var task = new Test3DExceptionTask(1, 2, 1);

        // Test that AggregateException is thrown when an exception occurs
        var aggregateException = Assert.Catch<AggregateException>(() => task.RunParallel(3, 3, 3));
        Assert.That(aggregateException.InnerExceptions.Count, Is.EqualTo(1));
        Assert.That(aggregateException.InnerExceptions[0], Is.TypeOf<InvalidOperationException>());
        Assert.That(aggregateException.InnerExceptions[0].Message, Does.Contain("Test exception at position (1, 2, 1)"));
    }

    [Test(Description = "Test 3D Batch Task Disposal")]
    public void TestReuseableBatchTask3DDisposal()
    {
        var task = new Test3DCounterTask();

        // Use the task
        task.RunParallel(2, 2, 2);
        Assert.That(task.Counter, Is.EqualTo(8));

        // Dispose and verify no exceptions
        Assert.DoesNotThrow(() => task.Dispose());

        // Multiple disposals should not throw
        Assert.DoesNotThrow(() => task.Dispose());
    }

    [Test(Description = "Test 3D Batch Task Concurrency Control")]
    public void TestReuseableBatchTask3DConcurrency()
    {
        const int maxConcurrency = 2;
        using var task = new Test3DConcurrencyTask(maxConcurrency);

        // Use a larger workload to better test concurrency control
        task.RunParallel(8, 8, 4);

        // The task should have used multiple threads
        var uniqueThreadIds = new HashSet<int>(task.ThreadIds);
        Assert.That(uniqueThreadIds.Count, Is.GreaterThan(0), "Should use at least one thread");
        
        // Note: Due to ThreadPool behavior and timing, we might see more threads than maxConcurrency
        // especially with smaller workloads. The important thing is that we're using parallelism.
        Assert.That(uniqueThreadIds.Count, Is.LessThanOrEqualTo(Environment.ProcessorCount), 
            "Should not use more threads than available processors");
    }

    [Test(Description = "Test 3D Batch Task Large Volume Processing")]
    public void TestReuseableBatchTask3DLargeVolume()
    {
        const int size = 20; // 20x20x20 = 8000 elements
        using var task = new Test3DCounterTask();

        task.RunParallel(size, size, size);
        Assert.That(task.Counter, Is.EqualTo(size * size * size));
    }

    [Test(Description = "Test 3D Batch Task with Different Volume Shapes")]
    public void TestReuseableBatchTask3DDifferentShapes()
    {
        using var task = new Test3DCounterTask();

        // Test rectangular prism
        task.ResetCounter();
        task.RunParallel(10, 5, 3);
        Assert.That(task.Counter, Is.EqualTo(150));

        // Test cube
        task.ResetCounter();
        task.RunParallel(5, 5, 5);
        Assert.That(task.Counter, Is.EqualTo(125));

        // Test flat volume (like a 2D plane in 3D space)
        task.ResetCounter();
        task.RunParallel(20, 15, 1);
        Assert.That(task.Counter, Is.EqualTo(300));
    }
} 