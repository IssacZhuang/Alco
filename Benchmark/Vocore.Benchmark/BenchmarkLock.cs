using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vocore.Unsafe;

namespace Vocore.Benchmark;

public class BenchmarkLock
{
    private object _lockObj;
    private Lock _lock;
    private AtomicSpinLock _atomicSpinLock;
    private AtomicSpinLockObject _atomicSpinLockObject;

    private ParallelOptions _options8Threads;
    private ParallelOptions _options16Threads;


    [GlobalSetup]
    public void Setup()
    {
        _lockObj = new object();
        _lock = new Lock();
        _atomicSpinLock = new AtomicSpinLock();
        _atomicSpinLockObject = new AtomicSpinLockObject();
        _options8Threads = new ParallelOptions();
        _options8Threads.MaxDegreeOfParallelism = 8;

        _options16Threads = new ParallelOptions();
        _options16Threads.MaxDegreeOfParallelism = 16;



    }

    [Benchmark(Description = "Lock single thread")]
    public void Lock()
    {
        lock (_lockObj)
        {
        }
    }

    [Benchmark(Description = ".Net 9 lock single thread")]
    public void DotNetLock()
    {
        using (_lock.EnterScope())
        {

        }
    }

    [Benchmark(Description = "AtomicSpinLock single thread")]
    public void AtomicSpinLock()
    {
        _atomicSpinLock.Lock();
        _atomicSpinLock.Unlock();
    }

    [Benchmark(Description = "AtomicSpinLockObject single thread")]
    public void AtomicSpinLockObject()
    {
        _atomicSpinLockObject.Lock();
        _atomicSpinLockObject.Unlock();
    }

    [Benchmark(Description = "Lock 8 threads")]
    public void Lock8Threads()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            lock (_lockObj)
            {
            }
        });
    }

    
    [Benchmark(Description = ".Net 9 lock 8 threads")]
    public void DotNetLock8Threads()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            using (_lock.EnterScope())
            {
            }
        });
    }

    [Benchmark(Description = "AtomicSpinLock 8 threads")]
    public void AtomicSpinLock8Threads()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            _atomicSpinLock.Lock();
            _atomicSpinLock.Unlock();
        });
    }

    [Benchmark(Description = "AtomicSpinLockObject 8 threads")]
    public void AtomicSpinLockObject8Threads()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            _atomicSpinLockObject.Lock();
            _atomicSpinLockObject.Unlock();
        });
    }

    [Benchmark(Description = "Lock 16 threads")]
    public void Lock16Threads()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            lock (_lockObj)
            {
            }
        });
    }

    [Benchmark(Description = ".Net 9 lock 16 threads")]
    public void DotNetLock16Threads()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            using (_lock.EnterScope())
            {
            }
        });
    }

    [Benchmark(Description = "AtomicSpinLock 16 threads")]
    public void AtomicSpinLock16Threads()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            _atomicSpinLock.Lock();
            _atomicSpinLock.Unlock();
        });
    }

    [Benchmark(Description = "AtomicSpinLockObject 16 threads")]
    public void AtomicSpinLockObject16Threads()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            _atomicSpinLockObject.Lock();
            _atomicSpinLockObject.Unlock();
        });
    }

}

public class BenchmarkLockAdd
{
    private object _lockObj;
    private Lock _lock;
    private AtomicSpinLock _atomicSpinLock;
    private AtomicSpinLockObject _atomicSpinLockObject;

    private ParallelOptions _options8Threads;
    private ParallelOptions _options16Threads;

    private List<int> _list;

    [GlobalSetup]
    public void Setup()
    {
        _lockObj = new object();
        _lock = new Lock();
        _atomicSpinLock = new AtomicSpinLock();
        _atomicSpinLockObject = new AtomicSpinLockObject();
        _options8Threads = new ParallelOptions();
        _options8Threads.MaxDegreeOfParallelism = 8;

        _options16Threads = new ParallelOptions();
        _options16Threads.MaxDegreeOfParallelism = 16;

        _list = new List<int>();
    }


    [Benchmark(Description = "Lock 8 threads add")]
    public void Lock8ThreadsAdd()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            lock (_lockObj)
            {
                _list.Add(1);
            }
        });
    }

    [Benchmark(Description = ".Net 9 lock 8 threads add")]
    public void DotNetLock8ThreadsAdd()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            using (_lock.EnterScope())
            {
                _list.Add(1);
            }
        });
    }

    [Benchmark(Description = "AtomicSpinLock 8 threads add")]
    public void AtomicSpinLock8ThreadsAdd()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            _atomicSpinLock.Lock();
            _list.Add(1);
            _atomicSpinLock.Unlock();
        });
    }

    [Benchmark(Description = "AtomicSpinLockObject 8 threads add")]
    public void AtomicSpinLockObject8ThreadsAdd()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            _atomicSpinLockObject.Lock();
            _list.Add(1);
            _atomicSpinLockObject.Unlock();
        });
    }

    [Benchmark(Description = "Lock 16 threads add")]
    public void Lock16ThreadsAdd()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            lock (_lockObj)
            {
                _list.Add(1);
            }
        });
    }

    [Benchmark(Description = ".Net 9 lock 16 threads add")]
    public void DotNetLock16ThreadsAdd()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            using (_lock.EnterScope())
            {
                _list.Add(1);
            }
        });
    }

    [Benchmark(Description = "AtomicSpinLock 16 threads add")]
    public void AtomicSpinLock16ThreadsAdd()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            _atomicSpinLock.Lock();
            _list.Add(1);
            _atomicSpinLock.Unlock();
        });
    }

    [Benchmark(Description = "AtomicSpinLockObject 16 threads add")]
    public void AtomicSpinLockObject16ThreadsAdd()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            _atomicSpinLockObject.Lock();
            _list.Add(1);
            _atomicSpinLockObject.Unlock();
        });
    }
}
