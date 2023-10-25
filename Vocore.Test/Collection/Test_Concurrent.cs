using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vocore;

namespace Vocore.Test
{
    public class Test_Concurrent
    {
        [Test("Test Circular Working Stealing Deque Push&Pop")]
        public void Test_CircularSinglePushPop()
        {
            int count = 1000000;
            CircularWorkStealingDeque<int> deque = new CircularWorkStealingDeque<int>(count);

            for (int i = 0; i < count; i++)
            {
                //UnitTest.PrintBlue("Push: " + i);
                deque.Push(i);
            }
            HashSet<int> result = new HashSet<int>();
            int poped = 0;
            for (int i = 0; i < count; i++)
            {
                StealingResult status = deque.TryPop(out int value);
                if (status == StealingResult.Success)
                {
                    poped++;
                    result.Add(value);
                }
            }
            UnitTest.PrintBlue("Poped: " + poped);
            int success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.Contains(i))
                {
                    success++;
                }
            }
            UnitTest.PrintBlue("success: " + success);
            UnitTest.AssertTrue(success == count);
        }

        [Test("Test Circular Working Stealing Deque Single Push&Steal")]
        public void Test_CircularSingleSteal()
        {
            int count = 1000000;
            CircularWorkStealingDeque<int> deque = new CircularWorkStealingDeque<int>(count);

            for (int i = 0; i < count; i++)
            {
                //UnitTest.PrintBlue("Push: " + i);
                deque.Push(i);
            }
            UnitTest.PrintBlue("Pushed: " + count);
            Dictionary<int, bool> result = new Dictionary<int, bool>();
            int poped = 0;
            for (int i = 0; i < count; i++)
            {
                StealingResult status = deque.TrySteal(out int value);
                if (status == StealingResult.Success)
                {
                    poped++;
                    result.TryAdd(value, true);
                }
            }
            UnitTest.PrintBlue("Poped: " + poped);
            int success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(i))
                {
                    success++;
                }
            }
            UnitTest.PrintBlue("success: " + success);
            UnitTest.AssertTrue(success == count);
        }

        [Test("Test Circular Working Stealing Single Push & Concurrent Steal")]
        public void Test_CircularConcurrentSteal()
        {
            int count = 1000000;
            CircularWorkStealingDeque<int> deque = new CircularWorkStealingDeque<int>(count);

            for (int i = 0; i < count; i++)
            {
                //UnitTest.PrintBlue("Push: " + i);
                deque.Push(i);
            }
            UnitTest.PrintBlue("Pushed: " + count);
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
            UnitTest.PrintBlue("Poped: " + poped);
            UnitTest.PrintBlue("Steal hit rate: " + ((float)poped)/stealCount);
            int success = 0;
            for (int i = 0; i < count; i++)
            {
                if (result.ContainsKey(i))
                {
                    success++;
                }
            }
            UnitTest.PrintBlue("success: " + success);
            UnitTest.AssertTrue(success == count);
        }

        [Test("Test Circular Working Stealing Deque vs .Net concurrent queue")]
        public void Test_WorkStealingDequeVsConcurrentQueue()
        {
            int count = 1000000;
            CircularWorkStealingDeque<int> deque = new CircularWorkStealingDeque<int>(count);
            ConcurrentQueue<int> queue = new ConcurrentQueue<int>();

            UnitTest.CheckGCAlloc("CircularWorkStealingDeque<int>.Push", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    deque.Push(i);
                }
            });

            UnitTest.CheckGCAlloc("ConcurrentQueue<int>.Enqueue", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    queue.Enqueue(i);
                }
            });

            UnitTest.Benchmark("CircularWorkStealingDeque<int>.TrySteal", () =>
            {
                Parallel.For(0, count, (i) =>
                {
                    while (true)
                    {
                        StealingResult result = deque.TrySteal(out int value);
                        if (result == StealingResult.Empty)
                        {
                            break;
                        }
                    }
                });
            });

            UnitTest.Benchmark("ConcurrentQueue<int>.TryDequeue", () =>
            {
                Parallel.For(0, count, (i) =>
                {
                    while (queue.TryDequeue(out int value))
                    {

                    }
                });
            });


        }

        [Test("Test Index Working Stealing Deque Pop&Steal")]
        public void Test_IndexWorkStealing()
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
            UnitTest.PrintBlue("success: " + success);
            UnitTest.AssertTrue(success == count);

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
            UnitTest.PrintBlue("success: " + success);
            UnitTest.AssertTrue(success == count);

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
            UnitTest.PrintBlue("steal: " + stealCount);
            UnitTest.PrintBlue("success: " + success);
            UnitTest.AssertTrue(success == count);
        }
    }
}

