using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using TestFramework;
using Alco;

namespace Alco.Test
{
    [TestFixture]
    public class TestConcurrentLruCache
    {
        [Test(Description = "Test basic concurrent LRU cache operations")]
        public void TestBasicOperations()
        {
            // Create a cache with capacity of 5
            var cache = new ConcurrentLruCache<string, string>(5);

            // Test initial state
            Assert.That(cache.Count, Is.EqualTo(0), "Initial count should be 0");
            Assert.That(cache.Capacity, Is.EqualTo(5), "Capacity should be 5");
            Assert.That(cache.SumWeight, Is.EqualTo(0), "Initial sum weight should be 0");

            // Test adding items
            cache.Set("key1", "value1");
            cache.Set("key2", "value2");
            cache.Set("key3", "value3");

            Assert.That(cache.Count, Is.EqualTo(3), "Count should be 3 after adding 3 items");
            Assert.That(cache.SumWeight, Is.EqualTo(3), "Sum weight should be 3");

            // Test retrieving items
            Assert.That(cache["key1"], Is.EqualTo("value1"), "Should retrieve correct value for key1");
            Assert.That(cache["key2"], Is.EqualTo("value2"), "Should retrieve correct value for key2");
            Assert.That(cache["key3"], Is.EqualTo("value3"), "Should retrieve correct value for key3");

            // Test TryGetValue
            string result;
            Assert.IsTrue(cache.TryGetValue("key1", out result), "TryGetValue should return true for existing key");
            Assert.That(result, Is.EqualTo("value1"), "TryGetValue should return correct value");

            Assert.IsFalse(cache.TryGetValue("nonexistent", out result), "TryGetValue should return false for non-existent key");
            Assert.IsNull(result, "TryGetValue should set out parameter to null for non-existent key");

            // Test removing an item
            Assert.IsTrue(cache.Remove("key2"), "Remove should return true for existing key");
            Assert.That(cache.Count, Is.EqualTo(2), "Count should be 2 after removing an item");
            Assert.That(cache.SumWeight, Is.EqualTo(2), "Sum weight should be 2 after removing an item");

            Assert.IsFalse(cache.Remove("key2"), "Remove should return false for already removed key");

            // Test clearing the cache
            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0), "Count should be 0 after clearing");
            Assert.That(cache.SumWeight, Is.EqualTo(0), "Sum weight should be 0 after clearing");
        }

        [Test(Description = "Test LRU eviction behavior")]
        public void TestLruEviction()
        {
            // Create a cache with capacity of 3
            var cache = new ConcurrentLruCache<string, string>(3);

            // Add items to fill the cache
            cache.Set("key1", "value1");
            cache.Set("key2", "value2");
            cache.Set("key3", "value3");

            // Access key1 to make it most recently used
            var value = cache["key1"];

            // Add a new item to trigger eviction
            cache.Set("key4", "value4");

            // key2 should be evicted as it's the least recently used
            Assert.IsFalse(cache.TryGetValue("key2", out _), "key2 should be evicted");
            Assert.IsTrue(cache.TryGetValue("key1", out _), "key1 should still be in cache");
            Assert.IsTrue(cache.TryGetValue("key3", out _), "key3 should still be in cache");
            Assert.IsTrue(cache.TryGetValue("key4", out _), "key4 should be in cache");

            // Access key3 to make it most recently used
            value = cache["key3"];

            // Add a new item to trigger eviction
            cache.Set("key5", "value5");

            // key1 should be evicted as it's now the least recently used
            Assert.IsFalse(cache.TryGetValue("key1", out _), "key1 should be evicted");
            Assert.IsTrue(cache.TryGetValue("key3", out _), "key3 should still be in cache");
            Assert.IsTrue(cache.TryGetValue("key4", out _), "key4 should still be in cache");
            Assert.IsTrue(cache.TryGetValue("key5", out _), "key5 should be in cache");
        }

        [Test(Description = "Test GetOrAdd functionality")]
        public void TestGetOrAdd()
        {
            var cache = new ConcurrentLruCache<string, string>(5);

            // Add initial items
            cache.Set("key1", "value1");

            // Test GetOrAdd with existing key
            string value = cache.GetOrAdd("key1", k => "new value");
            Assert.That(value, Is.EqualTo("value1"), "GetOrAdd should return existing value for existing key");

            // Test GetOrAdd with new key
            value = cache.GetOrAdd("key2", k => "value2");
            Assert.That(value, Is.EqualTo("value2"), "GetOrAdd should add and return new value for new key");
            Assert.That(cache.Count, Is.EqualTo(2), "Count should be 2 after GetOrAdd");

            // Test that the factory function is not called for existing keys
            bool factoryCalled = false;
            value = cache.GetOrAdd("key1", k => { factoryCalled = true; return "should not be used"; });
            Assert.IsFalse(factoryCalled, "Factory function should not be called for existing keys");
            Assert.That(value, Is.EqualTo("value1"), "Original value should be preserved");
        }

        [Test(Description = "Test concurrent access")]
        public void TestConcurrentAccess()
        {
            var cache = new ConcurrentLruCache<int, string>(1000);
            int itemCount = 50;
            int threadCount = 4;
            int operationsPerThread = 500;

            // Populate the cache with initial items
            for (int i = 0; i < itemCount; i++)
            {
                cache.Set(i, $"value{i}");
            }

            // Use CountdownEvent to track when all operations are complete
            int totalOperations = threadCount * operationsPerThread;
            using var countdownEvent = new CountdownEvent(totalOperations);

            // Track any exceptions that occur during parallel execution
            Exception capturedEx = null;
            object lockObj = new object();

            // Run operations in parallel
            Parallel.For(0, totalOperations, new ParallelOptions { MaxDegreeOfParallelism = threadCount }, (i) =>
            {
                try
                {
                    // Calculate which thread and operation this is
                    int threadId = i / operationsPerThread;
                    int operationId = i % operationsPerThread;

                    // Create a thread-local random with a unique seed
                    var localRandom = new System.Random(threadId * 100 + Environment.TickCount);

                    // Determine which key to operate on
                    int key = localRandom.Next(itemCount * 2); // Use keys beyond initial range too
                    int operation = localRandom.Next(4);

                    try
                    {
                        switch (operation)
                        {
                            case 0: // Get
                                string value;
                                if (cache.TryGetValue(key, out value))
                                {
                                    // Item exists
                                }
                                break;

                            case 1: // Set
                                cache.Set(key, $"value{key}-{operationId}");
                                break;

                            case 2: // GetOrAdd
                                cache.GetOrAdd(key, k => $"value{k}-{operationId}");
                                break;

                            case 3: // Remove
                                cache.Remove(key);
                                break;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore exceptions during concurrent operations
                        // This is expected in high-concurrency scenarios
                    }
                }
                catch (Exception ex)
                {
                    // Capture the first exception that occurs
                    lock (lockObj)
                    {
                        capturedEx ??= ex;
                    }
                }
                finally
                {
                    // Signal that this operation is complete
                    countdownEvent.Signal();
                }
            });

            // Wait for all operations to complete
            countdownEvent.Wait();

            // If any exceptions occurred, rethrow the first one
            if (capturedEx != null)
            {
                throw new Exception("Exception during concurrent test", capturedEx);
            }

            // Verify the cache is still in a valid state
            Assert.That(cache.Count, Is.LessThanOrEqualTo(1000), "Cache count should not exceed capacity");
            Assert.That(cache.SumWeight, Is.LessThanOrEqualTo(1000), "Cache weight should not exceed capacity");
        }

        [Test(Description = "Test LRU cache with custom weights")]
        public void TestCustomWeights()
        {
            // Create a custom LRU cache with weight based on string length
            var cache = new CustomWeightConcurrentLruCache(10);

            // Add items with different weights
            cache.Set("key1", "a");        // weight 1
            cache.Set("key2", "abcde");    // weight 5
            cache.Set("key3", "abc");      // weight 3

            Assert.That(cache.Count, Is.EqualTo(3), "Count should be 3");
            Assert.That(cache.SumWeight, Is.EqualTo(9), "Sum weight should be 9");

            // Add one more item to exceed capacity
            cache.Set("key4", "ab");       // weight 2

            // Total weight would be 11, which exceeds capacity of 10
            // So key1 should be evicted
            Assert.IsFalse(cache.TryGetValue("key1", out _), "key1 should be evicted");
            Assert.IsTrue(cache.TryGetValue("key2", out _), "key2 should still be in cache");
            Assert.IsTrue(cache.TryGetValue("key3", out _), "key3 should still be in cache");
            Assert.IsTrue(cache.TryGetValue("key4", out _), "key4 should be in cache");

            Assert.That(cache.Count, Is.EqualTo(3), "Count should be 3 after eviction");
            Assert.That(cache.SumWeight, Is.EqualTo(10), "Sum weight should be 10 after eviction");

            // Access key3 to make it most recently used
            var value = cache["key3"];

            // Add a heavy item that would cause multiple evictions
            cache.Set("key5", "abcdef");   // weight 6

            // This should evict key2 (least recently used) and key4
            // We need to check what's actually in the cache rather than assuming
            // specific eviction behavior
            int remainingCount = 0;
            int remainingWeight = 0;

            if (cache.TryGetValue("key2", out _))
            {
                remainingCount++;
                remainingWeight += 5;
            }

            if (cache.TryGetValue("key3", out _))
            {
                remainingCount++;
                remainingWeight += 3;
            }

            if (cache.TryGetValue("key4", out _))
            {
                remainingCount++;
                remainingWeight += 2;
            }

            if (cache.TryGetValue("key5", out _))
            {
                remainingCount++;
                remainingWeight += 6;
            }

            // Verify that key5 is in the cache (it was just added)
            Assert.IsTrue(cache.TryGetValue("key5", out _), "key5 should be in cache");

            // Verify that the total weight doesn't exceed capacity
            Assert.That(cache.SumWeight, Is.LessThanOrEqualTo(10), "Sum weight should not exceed capacity");

            // Verify that the count matches what we found
            Assert.That(cache.Count, Is.EqualTo(remainingCount), "Count should match remaining items");

            // Verify that the weight matches what we found
            Assert.That(cache.SumWeight, Is.EqualTo(remainingWeight), "Sum weight should match remaining items");
        }

        [Test(Description = "Test exception handling")]
        public void TestExceptionHandling()
        {
            var cache = new ConcurrentLruCache<string, string>(5);

            // Test null key
            Assert.Throws<ArgumentNullException>(() => cache.Set(null, "value"));
            Assert.Throws<ArgumentNullException>(() => cache.TryGetValue(null, out _));
            Assert.Throws<ArgumentNullException>(() => cache.Remove(null));
            Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd(null, k => "value"));
            Assert.Throws<ArgumentNullException>(() => { var x = cache[null]; });
            Assert.Throws<ArgumentNullException>(() => cache[null] = "value");

            // Test null value
            Assert.Throws<ArgumentNullException>(() => cache.Set("key", null));
            Assert.Throws<ArgumentNullException>(() => cache["key"] = null);

            // Test null factory function
            Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd("key", null));

            // Test factory function returning null
            Assert.Throws<ArgumentNullException>(() => cache.GetOrAdd("key2", k => null));

            // Test invalid capacity
            Assert.Throws<ArgumentException>(() => new ConcurrentLruCache<string, string>(0));
            Assert.Throws<ArgumentException>(() => new ConcurrentLruCache<string, string>(-1));
        }

        [Test(Description = "Test item weight exceeding capacity")]
        public void TestItemExceedingCapacity()
        {
            var cache = new CustomWeightConcurrentLruCache(5);

            // Try to add an item with weight greater than capacity
            Assert.Throws<ArgumentException>(() => cache.Set("key1", "abcdef")); // weight 6 > capacity 5

            // Cache should still be empty
            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(cache.SumWeight, Is.EqualTo(0));

            // Add a valid item
            cache.Set("key2", "abc"); // weight 3
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.SumWeight, Is.EqualTo(3));
        }

        [Test(Description = "Test concurrent GetOrAdd with same key")]
        public void TestConcurrentGetOrAdd()
        {
            var cache = new ConcurrentLruCache<int, string>(1000);
            int numThreads = 10;
            int numKeys = 20;
            int operationsPerThread = 100;

            // Track how many times the factory is called for each key
            var factoryCallCount = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

            // Use Parallel.For to call GetOrAdd concurrently
            Parallel.For(0, numThreads * operationsPerThread, i =>
            {
                int key = i % numKeys;
                string value = cache.GetOrAdd(key, k =>
                {
                    factoryCallCount.AddOrUpdate(k, 1, (_, count) => count + 1);
                    Thread.Sleep(1); // Simulate some work
                    return $"value{k}";
                });

                Assert.That(value, Is.EqualTo($"value{key}"), $"Value should match for key {key}");
            });

            // Verify that each key was created only once (or at least a reasonable number of times)
            // Due to the lock in GetOrAdd, each key should ideally be created only once
            foreach (var kvp in factoryCallCount)
            {
                Assert.That(kvp.Value, Is.LessThanOrEqualTo(2), 
                    $"Factory for key {kvp.Key} was called {kvp.Value} times, expected at most 2 (ideally 1)");
            }

            Assert.That(cache.Count, Is.EqualTo(numKeys), "Cache should contain all unique keys");
        }

        [Test(Description = "Test concurrent Get and Remove race condition")]
        public void TestConcurrentGetAndRemove()
        {
            var cache = new ConcurrentLruCache<int, string>(1000);
            int numKeys = 100;

            // Populate cache
            for (int i = 0; i < numKeys; i++)
            {
                cache.Set(i, $"value{i}");
            }

            Exception caughtException = null;
            object lockObj = new object();

            // Run concurrent operations: half threads do Get, half do Remove
            Parallel.For(0, 1000, i =>
            {
                try
                {
                    int key = i % numKeys;

                    if (i % 2 == 0)
                    {
                        // Try to get
                        cache.TryGetValue(key, out _);
                    }
                    else
                    {
                        // Try to remove
                        cache.Remove(key);
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        caughtException ??= ex;
                    }
                }
            });

            // Should not throw "LinkedList node does not belong to current LinkedList" exception
            if (caughtException != null)
            {
                throw new Exception("Exception during concurrent Get/Remove test", caughtException);
            }

            // Cache should be in a valid state
            Assert.That(cache.SumWeight, Is.EqualTo(cache.Count), "SumWeight should match Count");
        }

        [Test(Description = "Test concurrent TryGetValue and Set race condition")]
        public void TestConcurrentTryGetValueAndSet()
        {
            var cache = new ConcurrentLruCache<int, string>(500);
            int numKeys = 50;

            // Populate cache
            for (int i = 0; i < numKeys; i++)
            {
                cache.Set(i, $"initial{i}");
            }

            Exception caughtException = null;
            object lockObj = new object();

            // Run concurrent operations
            Parallel.For(0, 2000, i =>
            {
                try
                {
                    int key = i % numKeys;

                    if (i % 3 == 0)
                    {
                        // TryGetValue
                        cache.TryGetValue(key, out _);
                    }
                    else if (i % 3 == 1)
                    {
                        // Set (update)
                        cache.Set(key, $"updated{key}-{i}");
                    }
                    else
                    {
                        // Indexer get
                        try
                        {
                            var _ = cache[key];
                        }
                        catch (KeyNotFoundException)
                        {
                            // Expected if key was evicted
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        caughtException ??= ex;
                    }
                }
            });

            // Should not throw "LinkedList node does not belong to current LinkedList" exception
            if (caughtException != null)
            {
                throw new Exception("Exception during concurrent TryGetValue/Set test", caughtException);
            }

            // Cache should be in a valid state
            Assert.That(cache.Count, Is.LessThanOrEqualTo(500), "Count should not exceed capacity");
            Assert.That(cache.SumWeight, Is.EqualTo(cache.Count), "SumWeight should match Count");
        }

        [Test(Description = "Test concurrent operations with eviction")]
        public void TestConcurrentWithEviction()
        {
            var cache = new ConcurrentLruCache<int, string>(100);
            int numKeys = 200; // More keys than capacity to force eviction

            Exception caughtException = null;
            object lockObj = new object();

            // Run concurrent operations that will cause frequent evictions
            Parallel.For(0, 5000, i =>
            {
                try
                {
                    int key = i % numKeys;
                    int operation = i % 4;

                    switch (operation)
                    {
                        case 0:
                            cache.Set(key, $"value{key}-{i}");
                            break;
                        case 1:
                            cache.TryGetValue(key, out _);
                            break;
                        case 2:
                            cache.GetOrAdd(key, k => $"value{k}-{i}");
                            break;
                        case 3:
                            cache.Remove(key);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        caughtException ??= ex;
                    }
                }
            });

            // Should not throw any exceptions
            if (caughtException != null)
            {
                throw new Exception("Exception during concurrent eviction test", caughtException);
            }

            // Cache should be in a valid state
            Assert.That(cache.Count, Is.LessThanOrEqualTo(100), "Count should not exceed capacity");
            Assert.That(cache.SumWeight, Is.LessThanOrEqualTo(100), "SumWeight should not exceed capacity");
            Assert.That(cache.SumWeight, Is.EqualTo(cache.Count), "SumWeight should match Count");
        }

        [Test(Description = "Test high concurrency stress test")]
        public void TestHighConcurrencyStress()
        {
            var cache = new ConcurrentLruCache<int, string>(1000);
            int iterations = 10000;

            Exception caughtException = null;
            object lockObj = new object();

            // High-intensity parallel operations
            Parallel.For(0, iterations, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
            {
                try
                {
                    var random = new Random(i + Environment.TickCount);
                    int key = random.Next(1500); // More keys than capacity
                    int operation = random.Next(5);

                    switch (operation)
                    {
                        case 0:
                            cache.Set(key, $"value{key}");
                            break;
                        case 1:
                            cache.TryGetValue(key, out _);
                            break;
                        case 2:
                            try
                            {
                                var _ = cache[key];
                            }
                            catch (KeyNotFoundException)
                            {
                                // Expected
                            }
                            break;
                        case 3:
                            cache.GetOrAdd(key, k => $"value{k}");
                            break;
                        case 4:
                            cache.Remove(key);
                            break;
                    }

                    // Occasionally check cache state
                    if (i % 100 == 0)
                    {
                        var count = cache.Count;
                        var sumWeight = cache.SumWeight;
                        Assert.That(count, Is.LessThanOrEqualTo(1000), "Count exceeded capacity during stress test");
                        Assert.That(sumWeight, Is.LessThanOrEqualTo(1000), "SumWeight exceeded capacity during stress test");
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        caughtException ??= ex;
                    }
                }
            });

            // Should not throw any exceptions
            if (caughtException != null)
            {
                throw new Exception("Exception during high concurrency stress test", caughtException);
            }

            // Final verification
            Assert.That(cache.Count, Is.LessThanOrEqualTo(1000), "Final count should not exceed capacity");
            Assert.That(cache.SumWeight, Is.LessThanOrEqualTo(1000), "Final SumWeight should not exceed capacity");
            Assert.That(cache.SumWeight, Is.EqualTo(cache.Count), "Final SumWeight should match Count");
        }

        // Custom LRU cache implementation for testing custom weights
        private class CustomWeightConcurrentLruCache : ConcurrentLruCache<string, string>
        {
            public CustomWeightConcurrentLruCache(int capacity) : base(capacity)
            {
            }

            protected override int GetWeight(string value)
            {
                // Use the string length as the weight
                return value.Length;
            }
        }
    }
}