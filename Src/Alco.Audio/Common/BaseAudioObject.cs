using System;


namespace Alco;

/// <summary>
/// A base class for auto disposable objects. Usually used for objects that has complex life cycle.
/// </summary> 
public abstract class BaseAudioObject : IDisposable
{
    private volatile uint _disposed;

    public bool IsDisposed => _disposed != 0;

    ~BaseAudioObject()
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

    protected abstract void Dispose(bool disposing);
}