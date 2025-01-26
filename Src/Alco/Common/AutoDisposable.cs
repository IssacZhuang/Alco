using System;
using System.Threading;

namespace Alco;

/// <summary>
/// A base class for auto disposable objects. Usually used for objects that has complex life cycle.
/// </summary> 
public abstract class AutoDisposable : IDisposable
{
    private volatile uint _disposed;

    /// <summary>
    /// Gets a value indicating whether the object has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed != 0;

    ~AutoDisposable()
    {
        //On GC
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// Disposes the object, releasing any unmanaged resources. 
    /// This method is usually not called manually, as the object will be automatically collected by the garbage collector.
    /// </summary>
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