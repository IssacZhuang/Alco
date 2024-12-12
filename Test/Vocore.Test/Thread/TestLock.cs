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

        list.Clear();
        AtomicSpinLockObject lock2 = new AtomicSpinLockObject();
        Parallel.For(0, count, (i) =>
        {
            lock2.Lock();
            list.Add(i);
            lock2.Unlock();
        });
        Assert.That(list.Count, Is.EqualTo(count));

        list.Clear();
        Parallel.For(0, count, (i) =>
        {
            using (lock2.EnterScope())
            {
                list.Add(i);
            }
        });
        Assert.That(list.Count, Is.EqualTo(count));
    }
}