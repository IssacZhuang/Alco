using Alco.Graphics;
using Alco.Graphics.NoGPU;
using Alco.Rendering;

namespace Alco.Effects.Test;

internal static class Utility
{

    internal static DummyRenderingSystemHost CreateRenderingSystem(SlangFileResolver? resolver = null, GPUDevice? device = null)
    {
        GPUDevice gpuDevice = device ?? GraphicsDeviceFactory.GetNoGPUDevice();
        DummyRenderingSystemHost host = new DummyRenderingSystemHost();
        // Tests register module sources explicitly (GetShaderFromModule); a
        // resolver is only passed when a test serves importable files.
        RenderingSystem renderingSystem = new RenderingSystem(
            host,
            gpuDevice,
            PixelFormat.RGBA16Float,
            PixelFormat.Depth24PlusStencil8,
            resolver
        );
        host.RenderingSystem = renderingSystem;
        return host;
    }
}

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
