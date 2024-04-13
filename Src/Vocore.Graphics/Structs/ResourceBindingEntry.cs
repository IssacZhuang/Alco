namespace Vocore.Graphics;

public struct ResourceBindingEntry
{
    public ResourceBindingEntry(uint binding, IGPUBindableResource resource)
    {
        Binding = binding;
        Resource = resource;
        Offset = 0;
        Size = 0;
        UseOffset = false;
    }

    public ResourceBindingEntry(uint binding, IGPUBindableResource resource, uint offset, uint size)
    {
        Binding = binding;
        Resource = resource;
        Offset = offset;
        Size = size;
    }

    public uint Binding { get; init; }
    public IGPUBindableResource Resource { get; init; }
    public uint Offset { get; init; }
    public uint Size { get; init; }
    public bool UseOffset { get ; init; }
}