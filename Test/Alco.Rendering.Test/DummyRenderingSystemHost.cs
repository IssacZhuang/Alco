

namespace Alco.Rendering.Test;

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
    public RenderingSystem RenderingSystem { get; set; } = null!;

    public DummyRenderingSystemHost()
    {
        // The interface-required OnDispose must be initialized; OnUpdate's no-op
        // accessors drop subscribers instead.
        OnDispose += () => { };
    }

    public void Dispose()
    {
        OnDispose?.Invoke();
    }
}

