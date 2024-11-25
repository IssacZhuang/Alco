
namespace Vocore.Graphics.NoGPU;

internal class NoRenderPass : GPURenderPass
{
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoRenderPass(in RenderPassDescriptor descriptor) : base(descriptor)
    {
    }
    protected override void Dispose(bool disposing)
    {
        
    }
}