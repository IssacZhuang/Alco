namespace Vocore.Graphics;

public struct ResourceGroupDescriptor
{
    public GPUBindGroup Layout { get; init; }
    public ResourceBindingEntry[] Resources { get; init; }
}