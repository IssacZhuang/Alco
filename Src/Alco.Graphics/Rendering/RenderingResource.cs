// Base wrapper for GPU-backed rendering objects: GPU-safe IDisposable retirement by default
// with synchronous DestroyImmediate escape hatch and finalizer safety net.

using System.Diagnostics;

namespace Alco.Graphics;

/// <summary>Base class of wrappers that own GPU-backed state; disposed safely behind active GPU work by default.</summary>
public abstract class RenderingResource : IDisposable
{
    private const int StateLive = 0;
    private const int StateDisposed = 1;
    private const int StateReleased = 2;

    private int _state;
    private protected readonly RenderingSystem _system;
    internal readonly ulong BirthEpoch;

    /// <summary>Gets whether <see cref="Dispose"/> or <see cref="DestroyImmediate"/> has been called, or finalization has started.</summary>
    public bool IsDisposed => Volatile.Read(ref _state) != StateLive;

    /// <summary>Gets whether the underlying native GPU storage and descriptor slots have already been reclaimed.</summary>
    internal bool IsReleased => Volatile.Read(ref _state) == StateReleased;

    /// <summary>Legacy alias for <see cref="IsDisposed"/>.</summary>
    [Obsolete("Use IsDisposed instead.")]
    public bool IsDestroyed => IsDisposed;

    /// <summary>
    /// Marks this resource unusable and retires its underlying GPU memory after all overlapping recordings
    /// and submitted GPU work complete. Idempotent.
    /// </summary>
    public virtual void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, StateDisposed, StateLive) != StateLive)
            return;
        GC.SuppressFinalize(this);
        _system.RetireResource(this);
    }

    /// <summary>
    /// Immediately releases underlying GPU memory on the calling thread without timeline deferral.
    /// Caller must ensure all GPU work referencing this resource has completed.
    /// </summary>
    public void DestroyImmediate()
    {
        if (Interlocked.Exchange(ref _state, StateReleased) == StateReleased)
            return;
        GC.SuppressFinalize(this);
        Release();
    }

    /// <summary>Legacy alias for <see cref="Dispose"/>.</summary>
    [Obsolete("Use Dispose() for GPU-safe deferred retirement, or DestroyImmediate() when GPU work is known complete.")]
    public void Destroy() => Dispose();

    ~RenderingResource()
    {
        if (Interlocked.CompareExchange(ref _state, StateDisposed, StateLive) == StateLive)
            _system.PostReleaseFromFinalizer(this);
    }

    internal void Reclaim()
    {
        if (Interlocked.Exchange(ref _state, StateReleased) != StateReleased)
            Release();
    }

    /// <summary>Releases the native and shared state exactly once.</summary>
    protected abstract void Release();

    private protected RenderingResource(RenderingSystem system)
    {
        Debug.Assert(system != null);
        _system = system;
        BirthEpoch = system.ReadEpoch();
    }
}
