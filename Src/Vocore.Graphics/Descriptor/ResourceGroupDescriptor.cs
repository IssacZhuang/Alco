namespace Vocore.Graphics;

public struct ResourceGroupDescriptor
{
    public ResourceGroupDescriptor(
        GPUBindGroup layout,
        ResourceBindingEntry[] resources
    )
    {
        Layout = layout;
        Resources = resources;
    }

    public GPUBindGroup Layout { get; init; }
    public ResourceBindingEntry[] Resources { get; init; }
}