using System;
using System.Threading;

namespace Vocore;

/// <summary>
/// A base class for auto disposable objects. Usually used for objects that has complex life cycle.
/// </summary> 
public abstract class AutoDisposable : IDisposable
{
#if DEBUG 
    private readonly string _stackTraceOnCreate = Environment.StackTrace;
#endif

    private volatile uint _disposed;

    public bool IsDisposed => _disposed != 0;

    ~AutoDisposable()
    {
        //On GC
        if (!IsDisposed)
        {
#if DEBUG
            Log.Warning($"The object {GetType().Name} is been GC collected, try release it manually to improve performance. Stack Trace on creation: {_stackTraceOnCreate}");
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
    }

    protected abstract void Dispose(bool disposing);
}