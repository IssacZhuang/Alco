
namespace Alco.Graphics.NoGPU;

internal class NoBindGroup : GPUBindGroup
{
    protected override GPUDevice Device => NoDevice.noDevice;

    public override IReadOnlyList<BindGroupEntry> Bindings => Array.Empty<BindGroupEntry>();

    public NoBindGroup(in BindGroupDescriptor descriptor): base(descriptor)
    {
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}