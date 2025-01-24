using System;

namespace Vocore.Graphics;

public abstract class BaseGPUObject : IDisposable
{
    public string Name { get; }
    private volatile uint _disposed;
    //used for deffered disposal
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
            //Device.Destroy(this);
            //Device.Destroy will add this to a queue and dispose it at the end of the frame
            //it will cause memory leak
            //so it should be disposed immediately


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
        Dispose(true);
    }
    protected abstract void Dispose(bool disposing);

    private void LogGC()
    {
        Console.WriteLine($"GC {Name}, {GetType().Name}");
    }
}
