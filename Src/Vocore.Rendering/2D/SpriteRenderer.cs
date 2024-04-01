using Vocore.Graphics;

namespace Vocore.Rendering;

public class SpriteRenderer : AutoDisposable
{
    private GPUCommandBuffer _command;
    private GPUDevice _device;

    public SpriteRenderer()
    {
        _device = RendereringContext.Device;
        _command = _device.CreateCommandBuffer();
    }

    public void Begin(GPUFrameBuffer target)
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}