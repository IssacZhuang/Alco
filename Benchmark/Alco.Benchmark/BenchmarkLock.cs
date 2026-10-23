using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Alco;

namespace Alco.Benchmark;

/// <summary>
/// Benchmarks lock implementations under single-threaded and multi-threaded contention.
/// </summary>
public class BenchmarkLock
{
    private object _lockObj;
    private Lock _lock;
    private AtomicSpinLock _atomicSpinLock;
    private AtomicSpinLockObject _atomicSpinLockObject;

    private ParallelOptions _options8Threads;
    private ParallelOptions _options16Threads;


    /// <summary>
    /// Initializes the lock implementations and parallel options used by the benchmarks.
    /// </summary>
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

    /// <summary>
    /// Benchmarks acquiring and releasing an uncontended monitor lock.
    /// </summary>
    [Benchmark(Description = "Lock single thread")]
    public void Lock()
    {
        lock (_lockObj)
        {
        }
    }

    /// <summary>
    /// Benchmarks acquiring and releasing an uncontended <see cref="System.Threading.Lock"/>.
    /// </summary>
    [Benchmark(Description = ".NET Lock single thread")]
    public void DotNetLock()
    {
        using (_lock.EnterScope())
        {

        }
    }

    /// <summary>
    /// Benchmarks acquiring and releasing an uncontended atomic spin lock.
    /// </summary>
    [Benchmark(Description = "AtomicSpinLock single thread")]
    public void AtomicSpinLock()
    {
        _atomicSpinLock.Lock();
        _atomicSpinLock.Unlock();
    }

    /// <summary>
    /// Benchmarks acquiring and releasing an uncontended object-based atomic spin lock.
    /// </summary>
    [Benchmark(Description = "AtomicSpinLockObject single thread")]
    public void AtomicSpinLockObject()
    {
        _atomicSpinLockObject.Lock();
        _atomicSpinLockObject.Unlock();
    }

    /// <summary>
    /// Benchmarks monitor locking from eight concurrent threads.
    /// </summary>
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

    
    /// <summary>
    /// Benchmarks <see cref="System.Threading.Lock"/> from eight concurrent threads.
    /// </summary>
    [Benchmark(Description = ".NET Lock 8 threads")]
    public void DotNetLock8Threads()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            using (_lock.EnterScope())
            {
            }
        });
    }

    /// <summary>
    /// Benchmarks the atomic spin lock from eight concurrent threads.
    /// </summary>
    [Benchmark(Description = "AtomicSpinLock 8 threads")]
    public void AtomicSpinLock8Threads()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            _atomicSpinLock.Lock();
            _atomicSpinLock.Unlock();
        });
    }

    /// <summary>
    /// Benchmarks the object-based atomic spin lock from eight concurrent threads.
    /// </summary>
    [Benchmark(Description = "AtomicSpinLockObject 8 threads")]
    public void AtomicSpinLockObject8Threads()
    {
        Parallel.For(0, 1000, _options8Threads, (i) =>
        {
            _atomicSpinLockObject.Lock();
            _atomicSpinLockObject.Unlock();
        });
    }

    /// <summary>
    /// Benchmarks monitor locking from sixteen concurrent threads.
    /// </summary>
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

    /// <summary>
    /// Benchmarks <see cref="System.Threading.Lock"/> from sixteen concurrent threads.
    /// </summary>
    [Benchmark(Description = ".NET Lock 16 threads")]
    public void DotNetLock16Threads()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            using (_lock.EnterScope())
            {
            }
        });
    }

    /// <summary>
    /// Benchmarks the atomic spin lock from sixteen concurrent threads.
    /// </summary>
    [Benchmark(Description = "AtomicSpinLock 16 threads")]
    public void AtomicSpinLock16Threads()
    {
        Parallel.For(0, 1000, _options16Threads, (i) =>
        {
            _atomicSpinLock.Lock();
            _atomicSpinLock.Unlock();
        });
    }

    /// <summary>
    /// Benchmarks the object-based atomic spin lock from sixteen concurrent threads.
    /// </summary>
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

/// <summary>
/// Benchmarks list appends guarded by different lock implementations across one, eight, and sixteen threads.
/// </summary>
public class BenchmarkLockAdd
{
    private object _lockObj;
    private Lock _lock;
    private AtomicSpinLock _atomicSpinLock;
    private AtomicSpinLockObject _atomicSpinLockObject;

    private ParallelOptions _options1Thread;
    private ParallelOptions _options8Threads;
    private ParallelOptions _options16Threads;

    private List<int> _list;

    /// <summary>
    /// Initializes the lock implementations, parallel options, and target list used by the benchmarks.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _lockObj = new object();
        _lock = new Lock();
        _atomicSpinLock = new AtomicSpinLock();
        _atomicSpinLockObject = new AtomicSpinLockObject();
        _options1Thread = new ParallelOptions();
        _options1Thread.MaxDegreeOfParallelism = 1;

        _options8Threads = new ParallelOptions();
        _options8Threads.MaxDegreeOfParallelism = 8;

        _options16Threads = new ParallelOptions();
        _options16Threads.MaxDegreeOfParallelism = 16;

        _list = new List<int>();
    }


    /// <summary>
    /// Benchmarks single-threaded list appends without locking.
    /// </summary>
    [Benchmark(Baseline = true, Description = "No lock 1 thread add")]
    public void NoLock1ThreadAdd()
    {
        Parallel.For(0, 1000, _options1Thread, (i) =>
        {
            _list.Add(1);
        });
    }

    /// <summary>
    /// Benchmarks single-threaded list appends with a monitor lock.
    /// </summary>
    [Benchmark(Description = "Lock 1 thread add")]
    public void Lock1ThreadAdd()
    {
        Parallel.For(0, 1000, _options1Thread, (i) =>
        {
            lock (_lockObj)
            {
                _list.Add(1);
            }
        });
    }

    /// <summary>
    /// Benchmarks single-threaded list appends with <see cref="System.Threading.Lock"/>.
    /// </summary>
    [Benchmark(Description = ".NET Lock 1 thread add")]
    public void DotNetLock1ThreadAdd()
    {
        Parallel.For(0, 1000, _options1Thread, (i) =>
        {
            using (_lock.EnterScope())
            {
                _list.Add(1);
            }
        });
    }

    /// <summary>
    /// Benchmarks single-threaded list appends with an atomic spin lock.
    /// </summary>
    [Benchmark(Description = "AtomicSpinLock 1 thread add")]
    public void AtomicSpinLock1ThreadAdd()
    {
        Parallel.For(0, 1000, _options1Thread, (i) =>
        {
            _atomicSpinLock.Lock();
            _list.Add(1);
            _atomicSpinLock.Unlock();
        });
    }

    /// <summary>
    /// Benchmarks single-threaded list appends with an object-based atomic spin lock.
    /// </summary>
    [Benchmark(Description = "AtomicSpinLockObject 1 thread add")]
    public void AtomicSpinLockObject1ThreadAdd()
    {
        Parallel.For(0, 1000, _options1Thread, (i) =>
        {
            _atomicSpinLockObject.Lock();
            _list.Add(1);
            _atomicSpinLockObject.Unlock();
        });
    }

    /// <summary>
    /// Benchmarks list appends with eight concurrent threads and a monitor lock.
    /// </summary>
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

    /// <summary>
    /// Benchmarks list appends with eight concurrent threads and <see cref="System.Threading.Lock"/>.
    /// </summary>
    [Benchmark(Description = ".NET Lock 8 threads add")]
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

    /// <summary>
    /// Benchmarks list appends with eight concurrent threads and an atomic spin lock.
    /// </summary>
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

    /// <summary>
    /// Benchmarks list appends with eight concurrent threads and an object-based atomic spin lock.
    /// </summary>
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

    /// <summary>
    /// Benchmarks list appends with sixteen concurrent threads and a monitor lock.
    /// </summary>
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

    /// <summary>
    /// Benchmarks list appends with sixteen concurrent threads and <see cref="System.Threading.Lock"/>.
    /// </summary>
    [Benchmark(Description = ".NET Lock 16 threads add")]
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

    /// <summary>
    /// Benchmarks list appends with sixteen concurrent threads and an atomic spin lock.
    /// </summary>
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

    /// <summary>
    /// Benchmarks list appends with sixteen concurrent threads and an object-based atomic spin lock.
    /// </summary>
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

/// <summary>
/// Benchmarks synchronization primitives used for signaling and waiting.
/// </summary>
public class BenchmarkWait
{
    private int _count = 1000;
    private SemaphoreSlim[] _semaphores;
    private ManualResetEvent[] _manualResetEvents;
    private ManualResetEventSlim[] _manualResetEventSlims;
    private AutoResetEvent[] _autoResetEvents;
    private Barrier[] _barriers;

    /// <summary>
    /// Creates the synchronization primitives used for the current iteration.
    /// </summary>
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

    /// <summary>
    /// Benchmarks signaling and waiting with <see cref="System.Threading.SemaphoreSlim"/>.
    /// </summary>
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

    /// <summary>
    /// Benchmarks signaling and waiting with <see cref="System.Threading.ManualResetEvent"/>.
    /// </summary>
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

    /// <summary>
    /// Benchmarks signaling and waiting with <see cref="System.Threading.ManualResetEventSlim"/>.
    /// </summary>
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

    /// <summary>
    /// Benchmarks signaling and waiting with <see cref="System.Threading.AutoResetEvent"/>.
    /// </summary>
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


    /// <summary>
    /// Benchmarks signaling and waiting with <see cref="System.Threading.Barrier"/>.
    /// </summary>
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

