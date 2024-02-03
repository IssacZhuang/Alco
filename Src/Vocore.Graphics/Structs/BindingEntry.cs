namespace Vocore.Graphics;

public struct BindingEntry
{
    public BindingEntry(uint binding, ShaderStage stage, IGPUBindableResource resource)
    {
        Binding = binding;
        Resource = resource;
    }

    public uint Binding { get; init; }
    public IGPUBindableResource Resource { get; init; }
}