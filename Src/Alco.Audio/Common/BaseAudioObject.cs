using System;


namespace Alco;

/// <summary>
/// A base class for auto disposable objects. Usually used for objects that has complex life cycle.
/// </summary> 
public abstract class BaseAudioObject : IDisposable
{
    private volatile uint _disposed;

    /// <summary>Gets whether this object has been disposed.</summary>
    public bool IsDisposed => _disposed != 0;

    ~BaseAudioObject()
    {
        //On GC
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Dispose(false);
        }
    }

    /// <summary>Releases the object's resources.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    protected abstract void Dispose(bool disposing);
}