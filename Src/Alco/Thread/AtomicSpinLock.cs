using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Alco;

/// <summary>
/// A non-reentrant spin lock implemented with an atomic flag.
/// </summary>
public struct AtomicSpinLock
{
    private int _lock;

    /// <summary>
    /// Acquires the lock, spinning until it becomes available.
    /// </summary>
    public void Lock()
    {
        while (Interlocked.CompareExchange(ref _lock, 1, 0) != 0)
        {
            Thread.Yield();
        }
    }

    /// <summary>
    /// Releases the lock.
    /// </summary>
    public void Unlock()
    {
        Interlocked.Exchange(ref _lock, 0);
    }
}

/// <summary>
/// A reference-type wrapper around <see cref="AtomicSpinLock"/> that provides scoped locking.
/// </summary>
public class AtomicSpinLockObject
{
    /// <summary>
    /// A scope that releases the captured lock when disposed.
    /// </summary>
    public ref struct Scope : IDisposable
    {
        private AtomicSpinLockObject _lock;
        /// <summary>
        /// Captures the lock that is released when the scope is disposed.
        /// </summary>
        /// <param name="lock">The lock to release.</param>
        public Scope(AtomicSpinLockObject @lock) => _lock = @lock;
        /// <summary>
        /// Releases the captured lock.
        /// </summary>
        public readonly void Dispose() => _lock.Unlock();
    }

    private AtomicSpinLock _lock = new AtomicSpinLock();

    /// <summary>
    /// Acquires the underlying spin lock.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Lock() => _lock.Lock();

    /// <summary>
    /// Releases the underlying spin lock.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unlock() => _lock.Unlock();

    /// <summary>
    /// Acquires the lock and returns a scope that releases it when disposed.
    /// </summary>
    /// <returns>The scope that owns the acquired lock.</returns>
    public Scope EnterScope()
    {
        Lock();
        return new Scope(this);
    }
}