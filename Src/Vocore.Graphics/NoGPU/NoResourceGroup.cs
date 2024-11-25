
namespace Vocore.Graphics.NoGPU;

internal class NoResourceGroup : GPUResourceGroup
{
    public override IReadOnlyList<IGPUBindableResource> Resources => Array.Empty<IGPUBindableResource>();
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoResourceGroup(in ResourceGroupDescriptor descriptor): base(descriptor)
    {
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}