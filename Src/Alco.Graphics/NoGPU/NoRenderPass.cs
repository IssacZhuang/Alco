
namespace Alco.Graphics.NoGPU;

internal class NoRenderPass : GPUAttachmentLayout
{
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoRenderPass(in AttachmentLayoutDescriptor descriptor) : base(descriptor)
    {
    }
    protected override void Dispose(bool disposing)
    {
        
    }
}