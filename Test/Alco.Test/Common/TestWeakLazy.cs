using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Alco.Test;

/// <summary>
/// Unit tests for the <see cref="WeakLazy{T}"/> class.
/// </summary>
public class TestWeakLazy
{
    private class TestClass
    {
        public string Value { get; }

        public TestClass(string value)
        {
            Value = value;
        }
    }

    [Test]
    public void Constructor_WithNullValueFactory_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WeakLazy<TestClass>(null!));
    }

    [Test]
    public void Value_InitialAccess_CreatesNewInstance()
    {
        // Arrange
        var factoryCallCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            factoryCallCount++;
            return new TestClass("test");
        });

        // Act
        var value = weakLazy.Value;

        // Assert
        Assert.That(value, Is.Not.Null);
        Assert.That(value.Value, Is.EqualTo("test"));
        Assert.That(factoryCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Value_MultipleAccesses_ReturnsSameInstance()
    {
        // Arrange
        var factoryCallCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            factoryCallCount++;
            return new TestClass("test");
        });

        // Act
        var value1 = weakLazy.Value;
        var value2 = weakLazy.Value;

        // Assert
        Assert.That(value1, Is.SameAs(value2));
        Assert.That(factoryCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Value_AfterGarbageCollection_CreatesNewInstance()
    {
        // Arrange
        var factoryCallCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            factoryCallCount++;
            return new TestClass($"test{factoryCallCount}");
        });

        // Act - First access
        var value1 = weakLazy.Value;
        var firstValue = value1.Value;

        // Clear reference and force garbage collection
        value1 = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Second access after GC
        var value2 = weakLazy.Value;

        // Assert
        Assert.That(value2, Is.Not.Null);
        Assert.That(value2.Value, Is.EqualTo("test2"));
        Assert.That(factoryCallCount, Is.EqualTo(2));
    }

    [Test]
    public void Value_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var factoryCallCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            Interlocked.Increment(ref factoryCallCount);
            Thread.Sleep(10); // Simulate some work
            return new TestClass("test");
        });

        const int threadCount = 10;
        var tasks = new Task<TestClass>[threadCount];
        var results = new TestClass[threadCount];

        // Act - Multiple threads accessing Value simultaneously
        for (var i = 0; i < threadCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                results[index] = weakLazy.Value;
                return results[index];
            });
        }

        Task.WaitAll(tasks);

        // Assert - All threads should get the same instance
        var firstResult = results[0];
        for (var i = 1; i < threadCount; i++)
        {
            Assert.That(results[i], Is.SameAs(firstResult));
        }

        // Factory should only be called once
        Assert.That(factoryCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Value_FactoryThrowsException_PropagatesException()
    {
        // Arrange
        var weakLazy = new WeakLazy<TestClass>(() => throw new InvalidOperationException("Factory error"));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _ = weakLazy.Value);
        Assert.That(exception.Message, Is.EqualTo("Factory error"));
    }

    [Test]
    public void Value_FactoryThrowsException_CanRecoverOnNextAccess()
    {
        // Arrange
        var attemptCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
                throw new InvalidOperationException("First attempt fails");
            
            return new TestClass("success");
        });

        // Act & Assert - First access throws
        Assert.Throws<InvalidOperationException>(() => _ = weakLazy.Value);

        // Second access succeeds
        var value = weakLazy.Value;
        Assert.That(value, Is.Not.Null);
        Assert.That(value.Value, Is.EqualTo("success"));
    }

    [Test]
    public void Value_WeakReferencePreservesObject_ReturnsOriginalInstance()
    {
        // Arrange
        var factoryCallCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            factoryCallCount++;
            return new TestClass($"test{factoryCallCount}");
        });

        // Act - Keep a strong reference to prevent GC
        var strongReference = weakLazy.Value;
        var value1 = weakLazy.Value;
        var value2 = weakLazy.Value;

        // Force garbage collection (should not affect the object due to strong reference)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var value3 = weakLazy.Value;

        // Assert
        Assert.That(value1, Is.SameAs(strongReference));
        Assert.That(value2, Is.SameAs(strongReference));
        Assert.That(value3, Is.SameAs(strongReference));
        Assert.That(factoryCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Value_RapidGarbageCollectionAndAccess_HandlesCorrectly()
    {
        // Arrange
        var factoryCallCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            factoryCallCount++;
            return new TestClass($"test{factoryCallCount}");
        });

        // Act - Rapid access and garbage collection cycles
        for (var i = 0; i < 5; i++)
        {
            var value = weakLazy.Value;
            Assert.That(value, Is.Not.Null);
            
            // Don't keep reference to allow GC
            value = null;
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // Assert - Factory should have been called multiple times
        Assert.That(factoryCallCount, Is.GreaterThan(1));
    }

    [Test]
    public void Value_ConcurrentAccessAfterGC_ThreadSafe()
    {
        // Arrange
        var factoryCallCount = 0;
        var weakLazy = new WeakLazy<TestClass>(() =>
        {
            Interlocked.Increment(ref factoryCallCount);
            return new TestClass($"test{factoryCallCount}");
        });

        // First access and then clear reference
        var initialValue = weakLazy.Value;
        initialValue = null;
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int threadCount = 5;
        var tasks = new Task<TestClass>[threadCount];
        var results = new TestClass[threadCount];

        // Act - Multiple threads accessing after GC
        for (var i = 0; i < threadCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                results[index] = weakLazy.Value;
                return results[index];
            });
        }

        Task.WaitAll(tasks);

        // Assert - All threads should get the same new instance
        var firstResult = results[0];
        for (var i = 1; i < threadCount; i++)
        {
            Assert.That(results[i], Is.SameAs(firstResult));
        }

        // Factory should have been called twice (initial + after GC)
        Assert.That(factoryCallCount, Is.EqualTo(2));
    }
} 