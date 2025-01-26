using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Alco.Unsafe;

namespace Alco.Benchmark;

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

public class BenchmarkWait
{
    private int _count = 1000;
    private SemaphoreSlim[] _semaphores;
    private ManualResetEvent[] _manualResetEvents;
    private ManualResetEventSlim[] _manualResetEventSlims;
    private AutoResetEvent[] _autoResetEvents;
    private Barrier[] _barriers;

    [IterationSetup]
    public void Setup()
    {
        _semaphores = new SemaphoreSlim[_count];
        for (int i = 0; i < _count; i++)
        {
            _semaphores[i] = new SemaphoreSlim(0);
        }
        _manualResetEvents = new ManualResetEvent[_count];
        for (int i = 0; i < _count; i++)
        {
            _manualResetEvents[i] = new ManualResetEvent(false);
        }
        _manualResetEventSlims = new ManualResetEventSlim[_count];
        for (int i = 0; i < _count; i++)
        {
            _manualResetEventSlims[i] = new ManualResetEventSlim(false);
        }
        _autoResetEvents = new AutoResetEvent[_count];
        for (int i = 0; i < _count; i++)
        {
            _autoResetEvents[i] = new AutoResetEvent(false);
        }

        _barriers = new Barrier[_count];    
        for (int i = 0; i < _count; i++)
        {
            _barriers[i] = new Barrier(2);
        }

    }

    [Benchmark(Description = "SemaphoreSlim")]
    public void SemaphoreSlim()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _semaphores[index].Release();
            });
        }

        for (int i = 0; i < _count; i++)
        {
            _semaphores[i].Wait();
        }
    }

    [Benchmark(Description = "ManualResetEvent")]
    public void ManualResetEvent()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _manualResetEvents[index].Set();
            });
        }

        for (int i = 0; i < _count; i++)
        {
            _manualResetEvents[i].WaitOne();
        }
    }

    [Benchmark(Description = "ManualResetEventSlim")]
    public void ManualResetEventSlim()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _manualResetEventSlims[index].Set();
            });
        }

        for (int i = 0; i < _count; i++)
        {
            _manualResetEventSlims[i].Wait();
        }
    }

    [Benchmark(Description = "AutoResetEvent")]
    public void AutoResetEvent()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _autoResetEvents[index].Set();
            });
        }

        for (int i = 0; i < _count; i++)
        {
            _autoResetEvents[i].WaitOne();
        }
    }


    [Benchmark(Description = "Barrier")]    
    public void Barrier()
    {
        for (int i = 0; i < _count; i++)
        {
            int index = i;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                _barriers[index].SignalAndWait();
            });
        }

        for (int i = 0; i < _count; i++)
        {
            _barriers[i].SignalAndWait();
        }
    }


}

