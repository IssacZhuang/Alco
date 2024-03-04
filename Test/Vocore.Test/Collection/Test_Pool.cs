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
    }
}


