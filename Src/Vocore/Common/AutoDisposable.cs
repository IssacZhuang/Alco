using System;
using System.Threading;

namespace Vocore;

/// <summary>
/// A base class for auto disposable objects. Usually used for objects that has complex life cycle.
/// </summary> 
public abstract class AutoDisposable : IDisposable
{
    private volatile uint _disposed;

    public bool IsDisposed => _disposed != 0;

    ~AutoDisposable()
    {
        //On GC
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Dispose(false);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <inheritdoc cref="Dispose()" />
    /// <param name="disposing"><c>true</c> if the method was called from <see cref="Dispose()" />; otherwise, <c>false</c>.</param>
    protected abstract void Dispose(bool disposing);
}