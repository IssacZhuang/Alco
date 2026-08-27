using Alco.Rendering;

namespace Alco.World3D.Test;

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
    public RenderingSystem RenderingSystem { get; set; } = null!;

    public DummyRenderingSystemHost()
    {
        OnDispose += () => { };
    }

    public void Dispose()
    {
        OnDispose?.Invoke();
    }
}
