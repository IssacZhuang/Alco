
namespace Vocore.Graphics.NoGPU;

internal class NoResourceGroup : GPUResourceGroup
{
    public override string Name => "no_gpu_resource_group";

    public override IReadOnlyList<IGPUBindableResource> Resources => Array.Empty<IGPUBindableResource>();

    protected override void Dispose(bool disposing)
    {
        
    }
}