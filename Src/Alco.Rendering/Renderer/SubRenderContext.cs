

using Alco.Graphics;

namespace Alco.Rendering;

public sealed class SubRenderContext : AutoDisposable
{
    private readonly GPURenderBundle _renderBundle;

    public SubRenderContext(RenderingSystem renderingSystem, string name)
    {
        GPUDevice device = renderingSystem.GraphicsDevice;
        _renderBundle = device.CreateRenderBundle(new RenderBundleDescriptor(name));
    }

    protected override void Dispose(bool disposing)
    {

    }
}

