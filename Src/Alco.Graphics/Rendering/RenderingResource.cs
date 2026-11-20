// Base wrapper for GPU-backed rendering objects: exactly-once explicit destruction with a garbage-collected
// fallback that retires through the rendering system's release queue.

using System.Diagnostics;

namespace Alco.Graphics;

/// <summary>Base class of wrappers that own GPU-backed state; released exactly once, explicitly on the calling thread or through garbage collection.</summary>
public abstract class RenderingResource
{
    private int _destroyed;
    private protected readonly RenderingSystem _system;

    /// <summary>Gets whether this resource has been destroyed or has begun its garbage-collected release.</summary>
    public bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;

    /// <summary>Destroys the resource exactly once on the calling thread; call after its final GPU use.</summary>
    /// <remarks>Wrappers finalized by the garbage collector are reclaimed through <see cref="RenderingSystem.Tick"/> instead.</remarks>
    public void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            Debug.Assert(false);
            return;
        }
        Release();
        GC.SuppressFinalize(this);
    }

    ~RenderingResource()
    {
        if (Volatile.Read(ref _destroyed) == 0)
            _system.PostRelease(this);
    }

    internal void Reclaim()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) == 0)
            Release();
    }

    /// <summary>Releases the native and shared state exactly once.</summary>
    protected abstract void Release();

    private protected RenderingResource(RenderingSystem system)
    {
        Debug.Assert(system != null);
        _system = system;
    }
}
