using Alco.Rendering;

namespace Alco.World3D.Test;

/// <summary>A rendering system host stub for World3D tests.</summary>
public class DummyRenderingSystemHost : IRenderingSystemHost, IDisposable
{
    // The fake ignores host callbacks: update subscriptions are dropped rather
    // than stored, so the event declares its own empty accessors.
    public event Action<float> OnUpdate
    {
        add { }
        remove { }
    }

    public event Action OnDispose;

    // Never read before CreateRenderingSystem assigns it right after construction.
    /// <summary>Gets or sets the rendering system under test.</summary>
    public RenderingSystem RenderingSystem { get; set; } = null!;

    /// <summary>Creates the host and initializes the dispose event.</summary>
    public DummyRenderingSystemHost()
    {
        OnDispose += () => { };
    }

    /// <summary>Raises the dispose event.</summary>
    public void Dispose()
    {
        OnDispose?.Invoke();
    }
}
