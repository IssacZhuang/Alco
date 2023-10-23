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
                if (deque.TryPop(out int value))
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
                if (deque.TrySteal(out int value))
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
            Parallel.For(0, count, (i) =>
            {
                while (deque.HasContent)
                {
                    if (deque.TrySteal(out int value))
                    {
                        Interlocked.Increment(ref poped);
                        result.TryAdd(value, true);
                    }
                }
            });
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
    }
}

