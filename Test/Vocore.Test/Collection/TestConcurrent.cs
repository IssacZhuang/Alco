using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestFramework;
using Vocore;

namespace Vocore.Test
{
    public class TestConcurrent
    {
        [Test(Description = "Circular Working Stealing Deque Single Push&Steal")]
        public void TestCircularSingleSteal()
        {
            int count = 1000000;
            CircularWorkStealingDeque<int> deque = new CircularWorkStealingDeque<int>(count);

            for (int i = 0; i < count; i++)
            {
                //TestContext.WriteLine("Push: " + i);
                deque.Push(i);
            }
            TestContext.WriteLine("Pushed: " + count);
            HashSet<int> result = new HashSet<int>();
            int poped = 0;
            for (int i = 0; i < count; i++)
            {
                StealingResult status = deque.TrySteal(out int value);
                if (status == StealingResult.Success)
                {
                    poped++;
                    result.Add(value);
                }
            }
            TestContext.WriteLine("Poped: " + poped);
            int success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.Contains(i))
                {
                    success++;
                }
            }
            TestContext.WriteLine("success: " + success);
            Assert.IsTrue(success == count);
        }

        [Test(Description = "Circular Working Stealing Single Push & Concurrent Steal")]
        public void TestCircularConcurrentSteal()
        {
            int count = 1000000;
            CircularWorkStealingDeque<int> deque = new CircularWorkStealingDeque<int>(count);

            for (int i = 0; i < count; i++)
            {
                //TestContext.WriteLine("Push: " + i);
                deque.Push(i);
            }
            TestContext.WriteLine("Pushed: " + count);
            ConcurrentDictionary<int, bool> result = new ConcurrentDictionary<int, bool>();
            int poped = 0;
            int stealCount = 0;
            Parallel.For(0, count, (i) =>
            {
                while (true)
                {
                    StealingResult status = deque.TrySteal(out int value);
                    Interlocked.Increment(ref stealCount);
                    if (status == StealingResult.Empty)
                    {
                        break;
                    }
                    if (status == StealingResult.Success)
                    {
                        Interlocked.Increment(ref poped);
                        result.TryAdd(value, true);
                    }
                }
            });
            TestContext.WriteLine("Poped: " + poped);
            TestContext.WriteLine("Steal hit rate: " + ((float)poped)/stealCount);
            int success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(i))
                {
                    success++;
                }
            }
            TestContext.WriteLine("success: " + success);
            Assert.IsTrue(success == count);
        }


        [Test(Description ="Index Working Stealing Deque Pop&Steal")]
        public void TestIndexWorkStealing()
        {
            int count = 1000000;
            IndexWorkStealingDeque deque = new IndexWorkStealingDeque();
            deque.Set(0, count);
            ConcurrentDictionary<int, bool> result = new ConcurrentDictionary<int, bool>();
            //single thread steal
            for (int i = 0; i < count; i++)
            {
                if(deque.TrySteal(out int value) == StealingResult.Success)
                {
                    result.TryAdd(value, true);
                }
            }
            int success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(i))
                {
                    success++;
                }
            }
            TestContext.WriteLine("success: " + success);
            Assert.IsTrue(success == count);

            deque.Set(0, count);
            result.Clear();
            //single thread pop
            for (int i = 0; i < count; i++)
            {
                if (deque.TryPop(out int value) == StealingResult.Success)
                {
                    result.TryAdd(value, true);
                }
            }
            success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(i))
                {
                    success++;
                }
            }
            TestContext.WriteLine("success: " + success);
            Assert.IsTrue(success == count);

            //multi thread steal
            deque.Set(0, count);
            result.Clear();
            int stealCount = 0;

            Parallel.For(0, count, (i) =>
            {
                while (true)
                {
                    StealingResult stealResult = deque.TrySteal(out int value);
                    if (stealResult == StealingResult.Empty)
                    {
                        break;
                    }
                    if (stealResult == StealingResult.Success)
                    {
                        Interlocked.Increment(ref stealCount);
                        result.TryAdd(value, true);
                    }
                }
            });
            success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(i))
                {
                    success++;
                }
            }
            TestContext.WriteLine("steal: " + stealCount);
            TestContext.WriteLine("success: " + success);
            Assert.IsTrue(success == count);

            deque.Set(0, count);
            result.Clear();

            bool threadPopFinished = false;
            bool threadStealFinished = false;
            int stealed =0;
            int poped = 0;
            ManualResetEvent resetEvent = new ManualResetEvent(false);
            Thread threadPop = new Thread(() =>
            {
                resetEvent.WaitOne();
                while (true)
                {
                    StealingResult stealResult = deque.TryPop(out int value);
                    if (stealResult == StealingResult.Empty)
                    {
                        break;
                    }
                    if (stealResult == StealingResult.Success)
                    {
                        Volatile.Write(ref poped, Volatile.Read(ref poped) + 1);
                        result.TryAdd(value, true);
                    }
                }
                Volatile.Write(ref threadPopFinished, true);
            });

            Thread threadSteal = new Thread(() =>
            {
                resetEvent.WaitOne();
                while (true)
                {
                    StealingResult stealResult = deque.TrySteal(out int value);
                    if (stealResult == StealingResult.Empty)
                    {
                        break;
                    }
                    if (stealResult == StealingResult.Success)
                    {
                        Volatile.Write(ref stealed, Volatile.Read(ref stealed) + 1);
                        result.TryAdd(value, true);
                    }
                }
                Volatile.Write(ref threadStealFinished, true);
            });

            threadPop.Start();
            threadSteal.Start();
            resetEvent.Set();
            
            while (!Volatile.Read(ref threadPopFinished) || !Volatile.Read(ref threadStealFinished));

            success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(i))
                {
                    success++;
                }
            }
            TestContext.WriteLine("steal: " + stealed);
            TestContext.WriteLine("pop: " + poped);
            TestContext.WriteLine("success: " + success);
            Assert.IsTrue(success == count);

        }

        
    }
}

