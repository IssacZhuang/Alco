
namespace Alco.Graphics.NoGPU;

internal class NoAttachmentLayout : GPUAttachmentLayout
{
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoAttachmentLayout(in AttachmentLayoutDescriptor descriptor) : base(descriptor)
    {
    }
    protected override void Dispose(bool disposing)
    {
        
    }
}