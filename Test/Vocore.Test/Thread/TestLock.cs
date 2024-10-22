using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vocore.Test;

public class TestLock
{

    [Test(Description = "Test CAS lock")]
    public void TestCASLock()
    {
        AtomicSpinLock @lock = new AtomicSpinLock();
        int count = 1000000;
        List<int> list = new List<int>();
        Parallel.For(0, count, (i) =>
        {
            @lock.Lock();
            list.Add(i);
            @lock.Unlock();
        });
        Assert.That(list.Count, Is.EqualTo(count));
    }

    [Test(Description = "Test lock performance")]
    public void TestLockPerformance()
    {
        int count = 1000000;
        //no lock vs lock vs mutex
        List<int> list = new List<int>();
        //warm up
        for (int i = 0; i < count; i++)
        {
            list.Add(i);
        }
        list.Clear();

        //no lock
        UtilsTest.Benchmark("No Lock", () =>
        {
            for (int i = 0; i < count; i++)
            {
                list.Add(i);
            }
        });

        //lock single thread
        object lockObject = new object();
        UtilsTest.Benchmark("Lock single thread", () =>
        {
            for (int i = 0; i < count; i++)
            {
                lock (lockObject)
                {
                    list.Add(i);
                }
            }
        });

        //.net 9 lock
        Lock @lock = new Lock();
        UtilsTest.Benchmark(".net 9 Lock single thread", () =>
        {
            for (int i = 0; i < count; i++)
            {
                using(@lock.EnterScope()){
                    list.Add(i);
                }
            }
        });

        //cas lock single thread
        UtilsTest.Benchmark("CAS Lock single thread", () =>
        {
            AtomicSpinLock @lock = new AtomicSpinLock();
            for (int i = 0; i < count; i++)
            {
                @lock.Lock();
                list.Add(i);
                @lock.Unlock();
            }
        });

        //mutex single thread
        // banned: to slow
        // Mutex mutex = new Mutex();
        // UtilsTest.Benchmark("Mutex single thread", () =>
        // {
        //     for (int i = 0; i < count; i++)
        //     {
        //         mutex.WaitOne();
        //         list.Add(i);
        //         mutex.ReleaseMutex();
        //     }
        // });

        //lock multi thread
        UtilsTest.Benchmark("Lock multi thread", () =>
        {
            Parallel.For(0, count, (i) =>
            {
                lock (lockObject)
                {
                    list.Add(i);
                }
            });
        });

        // .net 9 lock multi thread
        UtilsTest.Benchmark(".net 9 Lock multi thread", () =>
        {
            Parallel.For(0, count, (i) =>
            {
                using(@lock.EnterScope()){
                    list.Add(i);
                }
            });
        });

        //cas lock multi thread
        UtilsTest.Benchmark("CAS Lock multi thread", () =>
        {
            Parallel.For(0, count, (i) =>
            {
                AtomicSpinLock @lock = new AtomicSpinLock();
                @lock.Lock();
                list.Add(i);
                @lock.Unlock();
            });
        });

        //mutex multi thread
        // UtilsTest.Benchmark("Mutex multi thread", () =>
        // {
        //     Parallel.For(0, count, (i) =>
        //     {
        //         mutex.WaitOne();
        //         list.Add(i);
        //         mutex.ReleaseMutex();
        //     });
        // });


        //custom scheduler
        using ParallelScheduler scheduler = new ParallelScheduler(8, "TestScheduler");
        //lock multi thread
        UtilsTest.Benchmark("Lock multi thread 2", () =>
        {
            scheduler.For(count, (i) =>
            {
                lock (lockObject)
                {
                    list.Add(i);
                }
            });
        });

        // .net 9 lock multi thread
        UtilsTest.Benchmark(".net 9 Lock multi thread 2", () =>
        {
            scheduler.For(count, (i) =>
            {
                using(@lock.EnterScope()){
                    list.Add(i);
                }
            });
        });

        //cas lock multi thread
        UtilsTest.Benchmark("CAS Lock multi thread 2", () =>
        {
            scheduler.For(count, (i) =>
            {
                AtomicSpinLock @lock = new AtomicSpinLock();
                @lock.Lock();
                list.Add(i);
                @lock.Unlock();
            });
        });

        //mutex multi thread
        // UtilsTest.Benchmark("Mutex multi thread 2", () =>
        // {
        //     scheduler.For(count, (i) =>
        //     {
        //         mutex.WaitOne();
        //         list.Add(i);
        //         mutex.ReleaseMutex();
        //     });
        // });

    }
}