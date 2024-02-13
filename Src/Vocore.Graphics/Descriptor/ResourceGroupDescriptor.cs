namespace Vocore.Graphics;

public struct ResourceGroupDescriptor
{
    public ResourceGroupDescriptor(
        GPUBindGroup layout,
        ResourceBindingEntry[] resources,
        string name = "unamed_resource_group"
    )
    {
        Layout = layout;
        Resources = resources;
        Name = name;
    }

    public GPUBindGroup Layout { get; init; }
    public ResourceBindingEntry[] Resources { get; init; }
    public string Name { get; init; } = "unamed_resource_group";
}