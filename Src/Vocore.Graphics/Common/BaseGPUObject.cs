using System;

namespace Vocore.Graphics;

public abstract class BaseGPUObject : IDisposable
{
    public abstract string Name { get; }
    private volatile uint _disposed;

    public bool IsDisposed => _disposed != 0;

    ~BaseGPUObject()
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
