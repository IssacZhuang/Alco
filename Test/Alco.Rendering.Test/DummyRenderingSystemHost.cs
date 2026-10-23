

namespace Alco.Rendering.Test;

/// <summary>A rendering system host stub for rendering tests.</summary>
public class DummyRenderingSystemHost : IRenderingSystemHost, IDisposable
{
    // Field-like event: raised by Dispose below, so its backing field is initialized
    // in the constructor.
    public event Action OnDispose;

    // This fake ignores per-frame host updates: subscribers are dropped, so the
    // event intentionally has no backing field to initialize.
    public event Action<float> OnUpdate
    {
        add { }
        remove { }
    }

    // Assigned by Utility.CreateRenderingSystem immediately after construction.
    /// <summary>Gets or sets the rendering system under test.</summary>
    public RenderingSystem RenderingSystem { get; set; } = null!;

    /// <summary>Creates the host and initializes the dispose event.</summary>
    public DummyRenderingSystemHost()
    {
        // The interface-required OnDispose must be initialized; OnUpdate's no-op
        // accessors drop subscribers instead.
        OnDispose += () => { };
    }

    /// <summary>Raises the dispose event.</summary>
    public void Dispose()
    {
        OnDispose?.Invoke();
    }
}

