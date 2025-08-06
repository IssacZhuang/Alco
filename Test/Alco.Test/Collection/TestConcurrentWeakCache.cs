using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using TestFramework;
using Alco;

namespace Alco.Test
{
    [TestFixture]
    public class TestConcurrentWeakCache
    {
        private class TestObject
        {
            public int Value { get; set; }
            public string Data { get; set; }

            public TestObject(int value, string data)
            {
                Value = value;
                Data = data;
                Log.Info($"TestObject created: Value={value}, Data={data}");
            }

            ~TestObject()
            {
                Log.Info($"TestObject finalized: Value={Value}, Data={Data}");
            }
        }

        [Test(Description = "Test basic GetOrAdd functionality")]
        public void TestBasicGetOrAdd()
        {
            var cache = new ConcurrentWeakCache<string, TestObject>();

            // Test adding new item
            var obj1 = cache.GetOrAdd("key1", key => new TestObject(123, "test1"));
            Assert.IsNotNull(obj1, "GetOrAdd should return non-null object");
            Assert.That(obj1.Value, Is.EqualTo(123), "Object should have correct value");
            Assert.That(obj1.Data, Is.EqualTo("test1"), "Object should have correct data");

            // Test retrieving same item
            var obj2 = cache.GetOrAdd("key1", key => new TestObject(456, "test2"));
            Assert.IsNotNull(obj2, "GetOrAdd should return non-null object for existing key");
            Assert.That(obj2.Value, Is.EqualTo(123), "Should return same object for existing key");
            Assert.That(obj2.Data, Is.EqualTo("test1"), "Should return same object for existing key");
            Assert.That(ReferenceEquals(obj1, obj2), Is.True, "Should return same object instance");

            // Test adding different key
            var obj3 = cache.GetOrAdd("key2", key => new TestObject(789, "test3"));
            Assert.IsNotNull(obj3, "GetOrAdd should return non-null object for new key");
            Assert.That(obj3.Value, Is.EqualTo(789), "Object should have correct value");
            Assert.That(obj3.Data, Is.EqualTo("test3"), "Object should have correct data");
        }

        [Test(Description = "Test weak reference behavior")]
        public void TestWeakReferenceBehavior()
        {
            var cache = new ConcurrentWeakCache<string, TestObject>();

            // Add an object to cache
            var obj = cache.GetOrAdd("key1", key => new TestObject(123, "test"));
            Assert.IsNotNull(obj, "GetOrAdd should return non-null object");

            // Remove strong reference
            obj = null;

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Try to get the object again - it should be recreated
            var newObj = cache.GetOrAdd("key1", key => new TestObject(456, "new test"));
            Assert.IsNotNull(newObj, "GetOrAdd should return non-null object after GC");
            Assert.That(newObj.Value, Is.EqualTo(456), "Should create new object after GC");
            Assert.That(newObj.Data, Is.EqualTo("new test"), "Should create new object after GC");
        }

        [Test(Description = "Test concurrent access")]
        public void TestConcurrentAccess()
        {
            var cache = new ConcurrentWeakCache<string, TestObject>();
            var results = new List<TestObject>();
            var tasks = new List<Task>();

            // Create multiple tasks that access the same key concurrently
            for (int i = 0; i < 10; i++)
            {
                int taskId = i;
                var task = Task.Run(() =>
                {
                    var obj = cache.GetOrAdd("concurrent_key", key => new TestObject(taskId, $"task_{taskId}"));
                    lock (results)
                    {
                        results.Add(obj);
                    }
                });
                tasks.Add(task);
            }

            // Wait for all tasks to complete
            Task.WaitAll(tasks.ToArray());

            Assert.That(results.Count, Is.EqualTo(10), "All tasks should complete");
            
            // All results should be the same object (first one created)
            var firstResult = results[0];
            foreach (var result in results)
            {
                Assert.That(ReferenceEquals(firstResult, result), Is.True, "All concurrent accesses should return same object");
            }
        }

        [Test(Description = "Test null valueFactory parameter")]
        public void TestNullValueFactory()
        {
            var cache = new ConcurrentWeakCache<string, TestObject>();

            Assert.Throws<ArgumentNullException>(() => 
                cache.GetOrAdd("key", null!), 
                "GetOrAdd should throw ArgumentNullException for null valueFactory");
        }

        [Test(Description = "Test with different key types")]
        public void TestDifferentKeyTypes()
        {
            var stringCache = new ConcurrentWeakCache<string, TestObject>();
            var intCache = new ConcurrentWeakCache<int, TestObject>();

            // Test string keys
            var stringObj = stringCache.GetOrAdd("string_key", key => new TestObject(1, key));
            Assert.IsNotNull(stringObj, "Should work with string keys");

            // Test int keys
            var intObj = intCache.GetOrAdd(42, key => new TestObject(key, $"int_{key}"));
            Assert.IsNotNull(intObj, "Should work with int keys");
            Assert.That(intObj.Value, Is.EqualTo(42), "Should use key as value");
            Assert.That(intObj.Data, Is.EqualTo("int_42"), "Should use key in data");
        }

        [Test(Description = "Test multiple keys and values")]
        public void TestMultipleKeysAndValues()
        {
            var cache = new ConcurrentWeakCache<string, TestObject>();

            // Add multiple items
            var obj1 = cache.GetOrAdd("key1", key => new TestObject(1, "data1"));
            var obj2 = cache.GetOrAdd("key2", key => new TestObject(2, "data2"));
            var obj3 = cache.GetOrAdd("key3", key => new TestObject(3, "data3"));

            Assert.IsNotNull(obj1, "First object should not be null");
            Assert.IsNotNull(obj2, "Second object should not be null");
            Assert.IsNotNull(obj3, "Third object should not be null");

            // Verify all objects are different
            Assert.That(ReferenceEquals(obj1, obj2), Is.False, "Different keys should return different objects");
            Assert.That(ReferenceEquals(obj1, obj3), Is.False, "Different keys should return different objects");
            Assert.That(ReferenceEquals(obj2, obj3), Is.False, "Different keys should return different objects");

            // Retrieve all objects again
            var retrieved1 = cache.GetOrAdd("key1", key => new TestObject(999, "wrong"));
            var retrieved2 = cache.GetOrAdd("key2", key => new TestObject(999, "wrong"));
            var retrieved3 = cache.GetOrAdd("key3", key => new TestObject(999, "wrong"));

            // Verify same objects are returned
            Assert.That(ReferenceEquals(obj1, retrieved1), Is.True, "Should return same object for key1");
            Assert.That(ReferenceEquals(obj2, retrieved2), Is.True, "Should return same object for key2");
            Assert.That(ReferenceEquals(obj3, retrieved3), Is.True, "Should return same object for key3");
        }

        [Test(Description = "Test valueFactory called only once per key")]
        public void TestValueFactoryCalledOnce()
        {
            var cache = new ConcurrentWeakCache<string, TestObject>();
            var callCount = 0;

            TestObject CreateObject(string key)
            {
                Interlocked.Increment(ref callCount);
                return new TestObject(callCount, $"call_{callCount}");
            }

            // First call should create object
            var obj1 = cache.GetOrAdd("key1", CreateObject);
            Assert.That(callCount, Is.EqualTo(1), "ValueFactory should be called once for first access");

            // Second call should return same object
            var obj2 = cache.GetOrAdd("key1", CreateObject);
            Assert.That(callCount, Is.EqualTo(1), "ValueFactory should not be called again for same key");
            Assert.That(ReferenceEquals(obj1, obj2), Is.True, "Should return same object instance");

            // Different key should call factory again
            var obj3 = cache.GetOrAdd("key2", CreateObject);
            Assert.That(callCount, Is.EqualTo(2), "ValueFactory should be called for different key");
        }

        [Test(Description = "Test with complex objects")]
        public void TestComplexObjects()
        {
            var cache = new ConcurrentWeakCache<string, List<int>>();

            var list1 = cache.GetOrAdd("list_key", key => new List<int> { 1, 2, 3, 4, 5 });
            Assert.IsNotNull(list1, "Should return non-null list");
            Assert.That(list1.Count, Is.EqualTo(5), "List should have correct count");

            // Modify the list
            list1.Add(6);
            Assert.That(list1.Count, Is.EqualTo(6), "List should be modifiable");

            // Get same list again
            var list2 = cache.GetOrAdd("list_key", key => new List<int> { 999 });
            Assert.That(ReferenceEquals(list1, list2), Is.True, "Should return same list instance");
            Assert.That(list2.Count, Is.EqualTo(6), "Should see modifications to original list");
        }

        [Test(Description = "Test stress test with many concurrent operations")]
        public void TestStressTest()
        {
            var cache = new ConcurrentWeakCache<int, TestObject>();
            var tasks = new List<Task>();
            var results = new List<TestObject>[100];

            // Create 100 tasks, each accessing 10 different keys
            for (int i = 0; i < 100; i++)
            {
                results[i] = new List<TestObject>();
                int taskId = i;
                var task = Task.Run(() =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        var key = taskId * 10 + j;
                        var obj = cache.GetOrAdd(key, k => new TestObject(k, $"task_{taskId}_key_{k}"));
                        lock (results[taskId])
                        {
                            results[taskId].Add(obj);
                        }
                    }
                });
                tasks.Add(task);
            }

            // Wait for all tasks to complete
            Task.WaitAll(tasks.ToArray());

            // Verify all tasks completed successfully
            for (int i = 0; i < 100; i++)
            {
                Assert.That(results[i].Count, Is.EqualTo(10), $"Task {i} should have 10 results");
            }
        }
    }
} 