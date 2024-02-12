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
        if (!IsDisposed)
        {
#if DEBUG
            GraphicsLogger.Warning($"The GPU Object {Name} is been GC collected, try release it manually to improve performance");
#endif
            Dispose();
        }
    }

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
