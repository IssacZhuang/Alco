namespace Alco.Graphics;

/// <summary>
/// Declares a resource group: the concrete resources bound against a bind group
/// layout for rendering.
/// </summary>
public struct ResourceGroupDescriptor
{
    /// <summary>
    /// Initializes the descriptor with the bind group layout and the resources
    /// bound to it.
    /// </summary>
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

    /// <summary>
    /// The bind group layout the resources are bound against; must be one of the
    /// layouts the pipeline was created with.
    /// </summary>
    public GPUBindGroup Layout { get; init; }
    /// <summary>
    /// The resources to bind, in binding order; their types must match the
    /// layout's entries.
    /// </summary>
    public ResourceBindingEntry[] Resources { get; init; }
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unamed_resource_group";
}