using System;

namespace Vocore.Graphics;

public abstract class BaseGPUObject : IDisposable
{
    private volatile uint _disposed;

    public bool IsDisposed => _disposed != 0;

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
