using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Alco;

public struct AtomicSpinLock
{
    private int _lock;

    public void Lock()
    {
        while (Interlocked.CompareExchange(ref _lock, 1, 0) != 0)
        {
            Thread.Yield();
        }
    }

    public void Unlock()
    {
        Interlocked.Exchange(ref _lock, 0);
    }
}

public class AtomicSpinLockObject
{
    public ref struct Scope : IDisposable
    {
        private AtomicSpinLockObject _lock;
        public Scope(AtomicSpinLockObject @lock) => _lock = @lock;
        public readonly void Dispose() => _lock.Unlock();
    }

    private AtomicSpinLock _lock = new AtomicSpinLock();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Lock() => _lock.Lock();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unlock() => _lock.Unlock();

    public Scope EnterScope()
    {
        Lock();
        return new Scope(this);
    }
}