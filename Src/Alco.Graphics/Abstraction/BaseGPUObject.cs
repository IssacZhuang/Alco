using System;

namespace Alco.Graphics;

public abstract class BaseGPUObject : IDisposable
{
    public string Name { get; }
    private volatile uint _disposed;
    // Tracks whether the native resource has been released. Separate from _disposed because
    // Dispose() only schedules a deferred native release; Destroy() can also be reached from
    // multiple paths (deferred queue, device shutdown, finalizer) and must release exactly once.
    private volatile uint _destroyed;
    /// <summary>
    /// The device used for deferred disposal of this object.
    /// </summary>
    protected abstract GPUDevice Device { get; }

    public bool IsDisposed => _disposed != 0;

    protected BaseGPUObject(string name)
    {
        Name = name;
    }

    ~BaseGPUObject()
    {
        //On GC
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
#if LOG_GPU_GC
            LogGC();
#endif


            try
            {
                Destroy();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in GPUObject({GetType().Name}) finalizer: {e}");
            }

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
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }
        _disposed = 1;
        GC.SuppressFinalize(this);
        Dispose(true);
    }
    protected abstract void Dispose(bool disposing);

    private void LogGC()
    {
        Console.WriteLine($"GC {Name}, {GetType().Name}");
    }
}
