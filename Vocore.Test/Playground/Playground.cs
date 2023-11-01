using Vocore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using System.Threading;
using System.Threading.Tasks;


using System.IO;

using System.Numerics;

namespace Vocore.Test
{
    delegate void TestDelegate();

    public struct TestJob : IJobBatch
    {
        public void Execute(int i)
        {
        }
    }

    public class Playground
    {
        //some temp code for testing
        [Test("Playground")]
        public unsafe void Test()
        {
            int count = 100000;
            int starupCount = 10000;
            int[] array = new int[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = i;
            }


            // UnitTest.Benchmark(() =>
            // {
            //     for (int i = 0; i < count; i++)
            //     {
            //         int temp = array[i];
            //     }
            // }, "for");

            ParallelScheduler.Instance.For(count, (i) =>
                {
                    int temp = array[i];
                });
            UnitTest.Benchmark(() =>
            {
                for (int i = 0; i < starupCount; i++)
                {
                    ParallelScheduler.Instance.For(count, (i) =>
                    {
                        int temp = array[i];
                    });
                }

            }, "parallel scheduler");
            Parallel.For(0, count, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
            {
                int temp = array[i];
            });
            UnitTest.Benchmark(() =>
            {
                for (int i = 0; i < starupCount; i++)
                {
                    Parallel.For(0, count, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
                    {
                        int temp = array[i];
                    });
                }
            }, "parallel for");

            // bool[] done = new bool[starupCount];
            // SpinLock doneLock = new SpinLock(false);
            // ManualResetEventSlim[] doneEvent = new ManualResetEventSlim[starupCount];
            // CountdownEvent doneCountEvent = new CountdownEvent(starupCount);
            // int doneCount = 0;
            // for (int i = 0; i < starupCount; i++)
            // {
            //     done[i] = false;
            //     doneEvent[i] = new ManualResetEventSlim(false);
            //     doneEvent[i].Reset();
            // }

            // for (int i = 0; i < starupCount; i++)
            // {
            //     ThreadPool.QueueUserWorkItem<int>((state) =>
            //     {
            //         Volatile.Write(ref done[state], true);
            //     }, i, true);
            // }
            // UnitTest.Benchmark(() =>
            // {
                
            //     for (int i = 0; i < starupCount; i++)
            //     {
            //         while (!Volatile.Read(ref done[i])) ;
            //     }
            // }, "volatile bool");

            // for (int i = 0; i < starupCount; i++)
            // {
            //     ThreadPool.QueueUserWorkItem<int>((state) =>
            //     {
            //         Interlocked.Increment(ref doneCount);
            //     }, i, true);
            // }
            // UnitTest.Benchmark(() =>
            // {
            //     // Parallel.For(0, starupCount, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
            //     // {
            //     //     Interlocked.Increment(ref doneCount);
            //     // });
                
            //     while (Volatile.Read(ref starupCount) < starupCount) ;
            // }, "interlocked");



            // for (int i = 0; i < starupCount; i++)
            // {
            //     ThreadPool.QueueUserWorkItem<int>((state) =>
            //     {
            //         doneEvent[state].Set();
            //     }, i, true);
            // }
            // UnitTest.Benchmark(() =>
            // {
            //     // Parallel.For(0, starupCount, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
            //     // {
            //     //     doneEvent[i].Set();
            //     // });
                
            //     for (int i = 0; i < starupCount; i++)
            //     {
            //         doneEvent[i].Wait();
            //     }
            // }, "reset event");

            // for (int i = 0; i < starupCount; i++)
            // {
            //     ThreadPool.QueueUserWorkItem<int>((state) =>
            //     {
            //         doneCountEvent.Signal();
            //     }, i, true);
            // }
            // UnitTest.Benchmark(() =>
            // {
            //     // Parallel.For(0, starupCount, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, (i) =>
            //     // {
            //     //     doneCountEvent.Signal();
            //     // });
                
            //     doneCountEvent.Wait();
            // }, "countdown event");


        }

        public void TestGeneric<T>(T data)
        {
            //Log.Info("TestGeneric: " + data);
        }
    }

}

