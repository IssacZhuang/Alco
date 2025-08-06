using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestFramework;
using Alco;

namespace Alco.Test
{
    [TestFixture]
    public class TestSlidingExpirationCache
    {
        [Test(Description = "Test basic operations of the sliding expiration cache")]
        public void TestBasicOperations()
        {
            // Create a cache with a sliding expiration of 1 second and eviction interval of 250ms
            var cache = new SlidingExpirationCache<string>(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250));

            // Add and retrieve a value
            string value = cache.GetOrAdd<string>("key1", k => "value1");
            Assert.That(value, Is.EqualTo("value1"), "Should return the added value");

            // Retrieve the same value again
            value = cache.GetOrAdd<string>("key1", k => "different value");
            Assert.That(value, Is.EqualTo("value1"), "Should return existing value, not create a new one");

            // Add another value
            value = cache.GetOrAdd<string>("key2", k => "value2");
            Assert.That(value, Is.EqualTo("value2"), "Should return the second added value");
        }

        [Test(Description = "Test that entries expire after sliding expiration time")]
        public void TestEntriesExpire()
        {
            // Create a cache with a sliding expiration of 200ms and eviction interval of 100ms
            var cache = new SlidingExpirationCache<string>(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(100));

            // Add a value
            string initialValue = "initial";
            cache.GetOrAdd<string>("key1", k => initialValue);

            // Wait for sliding expiration to elapse
            Thread.Sleep(300); // 300ms > 200ms sliding expiration

            // First call to trigger eviction check
            bool factoryCalled = false;
            cache.GetOrAdd<string>("key2", k => "dummy value");

            // Give a little time for eviction to complete
            Thread.Sleep(50);

            // Try to access the key again - it should create a new value
            string result = cache.GetOrAdd<string>("key1", k =>
            {
                factoryCalled = true;
                return "new value";
            });

            Assert.IsTrue(factoryCalled, "Value factory should be called after expiration");
            Assert.That(result, Is.EqualTo("new value"), "Should return the new value after expiration");
        }

        [Test(Description = "Test that active entries don't expire due to sliding window")]
        public void TestActiveEntriesStayAlive()
        {
            // Create a cache with a sliding expiration of 300ms and eviction interval of 150ms
            var cache = new SlidingExpirationCache<string>(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(150));

            // Add a value
            string initialValue = "initial";
            cache.GetOrAdd<string>("key1", k => initialValue);

            // Keep accessing the value within the sliding window to keep it alive
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(100); // 100ms < 300ms sliding expiration
                string value = cache.GetOrAdd<string>("key1", k => "should not be called");
                Assert.That(value, Is.EqualTo(initialValue), $"Value should still be alive at iteration {i}");
            }

            // Verify the value factory wasn't called during the loop
            bool factoryCalled = false;
            string result = cache.GetOrAdd<string>("key1", k =>
            {
                factoryCalled = true;
                return "new value";
            });

            Assert.IsFalse(factoryCalled, "Value factory should not be called for active entry");
            Assert.That(result, Is.EqualTo(initialValue), "Should return the initial value for active entry");
        }

        [Test(Description = "Test clear method")]
        public void TestClear()
        {
            // Create a cache
            var cache = new SlidingExpirationCache<string>(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250));

            // Add some values
            cache.GetOrAdd<string>("key1", k => "value1");
            cache.GetOrAdd<string>("key2", k => "value2");

            // Clear the cache
            cache.Clear();

            // Verify that the values are gone
            bool factoryCalled = false;
            cache.GetOrAdd<string>("key1", k =>
            {
                factoryCalled = true;
                return "new value";
            });

            Assert.IsTrue(factoryCalled, "Value factory should be called after clearing");
        }

        [Test(Description = "Test with null values")]
        public void TestNullValues()
        {
            // Create a cache
            var cache = new SlidingExpirationCache<string>(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(250));

            // Add a null value
            string value = cache.GetOrAdd<string>("key1", k => null);
            Assert.That(value, Is.Null, "Should allow storing null values");

            // Retrieve the null value
            value = cache.GetOrAdd<string>("key1", k => "not null");
            Assert.That(value, Is.Null, "Should retrieve null value correctly");
        }

        // [Test(Description = "Test concurrent access")]
        // public void TestConcurrentAccess()
        // {
        //     // Create a cache with a longer expiration to reduce test flakiness
        //     var cache = new SlidingExpirationCache<string>(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(500));

        //     // Use a distinct key range for each thread to avoid interference
        //     int keyCount = 50;
        //     int threadCount = 4;
        //     int[] accessCounts = new int[keyCount * threadCount];

        //     // Parallel access to the cache
        //     Parallel.For(0, threadCount, threadId =>
        //     {
        //         // Each thread accesses its own key range
        //         int startKey = threadId * keyCount;
        //         int endKey = startKey + keyCount;

        //         // FastRandom delay to increase chance of concurrent access
        //         var random = new System.FastRandom(threadId);

        //         for (int i = 0; i < 100; i++)
        //         {
        //             for (int key = startKey; key < endKey; key++)
        //             {
        //                 try
        //                 {
        //                     string keyString = $"key{key}";
        //                     // Each thread reads and updates its own keys
        //                     string result = cache.GetOrAdd<string>(keyString, k => "0");
        //                     int count = 0;
        //                     if (int.TryParse(result, out count))
        //                     {
        //                         // Update the value (in a real scenario)
        //                         // cache.Set would be called here
        //                     }

        //                     // Access and update
        //                     Thread.Sleep(random.Next(1, 3));

        //                     // Record this access
        //                     Interlocked.Increment(ref accessCounts[key]);
        //                 }
        //                 catch (Exception)
        //                 {
        //                     // Ignore any concurrent exceptions
        //                 }
        //             }
        //         }
        //     });

        //     // Verify that all keys were accessed
        //     for (int i = 0; i < accessCounts.Length; i++)
        //     {
        //         Assert.That(accessCounts[i], Is.GreaterThan(0), $"Key {i} should have been accessed");
        //     }
        // }
    }
}