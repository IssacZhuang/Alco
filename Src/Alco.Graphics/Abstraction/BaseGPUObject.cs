using System;

namespace Alco.Graphics;

/// <summary>
/// Provides exactly-once release and device-managed deferred disposal for GPU resources.
/// </summary>
public abstract class BaseGPUObject : IDisposable
{
    /// <summary>Gets the diagnostic name of the resource.</summary>
    public string Name { get; }

    private volatile uint _disposed;
    // Disposal schedules release; destruction claims the actual native cleanup independently.
    private volatile uint _destroyed;

    /// <summary>
    /// Gets the device used for deferred disposal of this object.
    /// </summary>
    protected abstract GPUDevice Device { get; }

    /// <summary>Gets whether disposal has been scheduled or release has been claimed.</summary>
    public bool IsDisposed => _disposed != 0;

    /// <summary>Initializes the diagnostic name of the resource.</summary>
    /// <param name="name">The diagnostic resource name.</param>
    protected BaseGPUObject(string name)
    {
        Name = name;
    }

    ~BaseGPUObject()
    {
        // A failed scheduling attempt must not prevent finalizer cleanup.
        if (_destroyed == 0)
        {
#if LOG_GPU_GC
            LogGC();
#endif
            try
            {
                Destroy(false);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error in GPUObject({GetType().Name}) finalizer: {e}");
            }
        }
    }

    /// <summary>
    /// Schedules resource release once, or releases immediately when the device's disposal queue is closed.
    /// A failed scheduling attempt may be retried and does not suppress finalizer cleanup.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Device.Destroy(this);
            GC.SuppressFinalize(this);
        }
        catch
        {
            if (_destroyed == 0)
            {
                Interlocked.CompareExchange(ref _disposed, 0, 1);
                // Preserve the disposed state if an immediate release raced with the reset.
                if (_destroyed != 0)
                {
                    _disposed = 1;
                }
            }
            throw;
        }
    }

    /// <summary>Claims resource release once and invokes the appropriate cleanup path.</summary>
    /// <param name="disposing">Whether cleanup was requested explicitly rather than by the finalizer.</param>
    internal void Destroy(bool disposing = true)
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }
        _disposed = 1;
        GC.SuppressFinalize(this);
        Dispose(disposing);
    }

    /// <summary>
    /// Releases the owned resources. Native handles must be consumed even when cleanup throws.
    /// Finalizer cleanup must not finish command recording or require the device to remain open.
    /// </summary>
    /// <param name="disposing">Whether cleanup was requested explicitly rather than by the finalizer.</param>
    protected abstract void Dispose(bool disposing);

    private void LogGC()
    {
        Console.WriteLine($"GC {Name}, {GetType().Name}");
    }
}
