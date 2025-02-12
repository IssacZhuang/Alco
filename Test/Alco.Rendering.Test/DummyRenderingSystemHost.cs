

namespace Alco.Rendering.Test;

public class DummyRenderingSystemHost : IRenderingSystemHost, IDisposable
{
    public event Action<float> OnUpdate;
    public event Action OnDispose;

    public RenderingSystem RenderingSystem { get; set; }

    public void Dispose()
    {
        OnDispose?.Invoke();
    }
}

