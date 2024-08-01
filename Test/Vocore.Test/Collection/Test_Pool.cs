using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Vocore.Test
{
    public class Test_Pool
    {
        [Test(Description = "ArrayPool")]
        public void Test_ArrayPool()
        {
            Pool<object> pool = new Pool<object>(10);

            object[] array = new object[10];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = new object();
            }

            for (int i = 0; i < array.Length; i++)
            {
                pool.TryReturn(array[i]);
            }

            //reach the capacity so it will not store the object
            pool.TryReturn(new object());

            for (int i = array.Length - 1; i >= 0; i--)
            {
                if (pool.TryGet(out object item))
                {
                    if (item != array[i])
                    {
                        Assert.Fail("ArrayPool get failed");
                    }
                }
                else
                {
                    Assert.Fail("ArrayPool get failed");
                }
            }

            //the pool is empty now so expect to get null
            if (pool.TryGet(out object item2))
            {
                Assert.Fail("ArrayPool get failed");
            }

        }

        [Test(Description = "ConcurrentPool single thread test")]
        public void Test_ConcurrentPool()
        {
            ConcurrentPool<object> pool = new ConcurrentPool<object>(10);

            object[] array = new object[10];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = new object();
            }

            for (int i = 0; i < array.Length; i++)
            {
                pool.TryReturn(array[i]);
            }

            //reach the capacity so it will not store the object
            pool.TryReturn(new object());

            for (int i = array.Length - 1; i >= 0; i--)
            {
                if (pool.TryGet(out object item))
                {
                    if (item != array[i])
                    {
                        Assert.Fail("ConcurrentPool get failed");
                    }
                }
                else
                {
                    Assert.Fail("ConcurrentPool get failed 2");
                }
            }

            //the pool is empty now so expect to get null
            if (pool.TryGet(out object item2))
            {
                Assert.Fail("ConcurrentPool get failed");
            }
        }

        [Test(Description = "ConcurrentPool multi thread test")]
        public void Test_ConcurrentPool_MultiThread()
        {
            int count = 1000;
            ConcurrentPool<object> pool = new ConcurrentPool<object>(count);

            ConcurrentStack<object> bag = new ConcurrentStack<object>();

            
            object[] array = new object[count];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = new object();
            }

            Parallel.For(0, count, (i) =>
            {
                pool.TryReturn(array[i]);
            });

            Assert.IsTrue(pool.Count == count);

            Parallel.For(0, count, (i) =>
            {
                if (pool.TryGet(out object item))
                {
                    bag.Push(item);
                }
            });

            Assert.IsTrue(bag.Count == count);
        }

        [Test(Description = "ConcurrentPool vs concurrent stack")]
        public void Test_ConcurrentPool_Vs_ConcurrentStack()
        {
            int count = 1000000;
            ConcurrentPool<object> pool = new ConcurrentPool<object>(count);

            ConcurrentStack<object> stack = new ConcurrentStack<object>();

            ConcurrentBag<object> bag = new ConcurrentBag<object>();


            object[] array = new object[count];
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = new object();
            }

            UtilsTest.Benchmark("ConcurrentPool", () =>
            {
                Parallel.For(0, count, (i) =>
                {
                    pool.TryReturn(array[i]);
                });

                Parallel.For(0, count, (i) =>
                {
                    pool.TryGet(out object item);

                });
            });


            UtilsTest.Benchmark("ConcurrentStack", () =>
            {

                Parallel.For(0, count, (i) =>
                {
                    stack.Push(array[i]);
                });

                Parallel.For(0, count, (i) =>
                {
                    stack.TryPop(out object item);

                });
            });

            UtilsTest.Benchmark("ConcurrentBag", () =>
            {
                Parallel.For(0, count, (i) =>
                {
                    bag.Add(array[i]);
                });

                Parallel.For(0, count, (i) =>
                {
                    bag.TryTake(out object item);
                });
            });
        }
    }
}


