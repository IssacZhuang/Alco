using System;

namespace Vocore.Graphics;

public abstract class BaseGPUObject : IDisposable
{
    public abstract string Name { get; }
    private volatile uint _disposed;
    //used for deffered disposal
    protected abstract GPUDevice Device { get; }

    public bool IsDisposed => _disposed != 0;

    ~BaseGPUObject()
    {
        //On GC
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
#if LOG_GPU_GC
            LogGC();
#endif
            Device.Destroy(this);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Device.Destroy(this);
            GC.SuppressFinalize(this);
        }
    }

    internal void Destroy()
    {
        Dispose(true);
    }
    protected abstract void Dispose(bool disposing);

    private void LogGC()
    {
        GraphicsLogger.Info($"GC {Name}, {GetType().Name}");
    }
}
