using System;

namespace Vocore.Graphics;

public abstract class BaseGPUObject : IDisposable
{
    public abstract string Name { get; }
    private volatile uint _disposed;

    public bool IsDisposed => _disposed != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        else
        {
            throw new ObjectDisposedException($"Trying to dispose an already disposed object: {Name}");
        }
    }

    protected abstract void Dispose(bool disposing);
}
