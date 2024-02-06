namespace Vocore.Graphics;

public struct ResourceBindingEntry
{
    public ResourceBindingEntry(uint binding, IGPUBindableResource resource)
    {
        Binding = binding;
        Resource = resource;
    }

    public uint Binding { get; init; }
    public IGPUBindableResource Resource { get; init; }
}