namespace Alco.Graphics;

/// <summary>
/// Declares a bind group: the set of binding slots (number, stage visibility and
/// resource type) shaders expect. Resource groups bound against it must provide
/// matching resources.
/// </summary>
public struct BindGroupDescriptor
{
    /// <summary>
    /// Initializes the descriptor with its layout entries.
    /// </summary>
    public BindGroupDescriptor(
        BindGroupEntry[] bindings,
        string name = "unnamed_bind_group")
    {
        Name = name;
        Bindings = bindings;
    }

    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed_bind_group";
    /// <summary>
    /// The layout entries the pipeline was created with; resources bound against
    /// this group must match them.
    /// </summary>
    public BindGroupEntry[] Bindings { get; init; }
}