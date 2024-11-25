
namespace Vocore.Graphics.NoGPU;

internal class NoRenderPass : GPURenderPass
{
    public override string Name => "no_gpu_render_pass";
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoRenderPass(in RenderPassDescriptor descriptor) : base(descriptor)
    {
    }
    protected override void Dispose(bool disposing)
    {
        
    }
}