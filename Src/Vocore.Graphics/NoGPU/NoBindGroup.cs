
namespace Vocore.Graphics.NoGPU;

internal class NoBindGroup : GPUBindGroup
{
    public override string Name => "no_gpu_bind_group";
    protected override GPUDevice Device => NoDevice.noDevice;

    public override IReadOnlyList<BindGroupEntry> Bindings => Array.Empty<BindGroupEntry>();

    protected override void Dispose(bool disposing)
    {
        
    }
}