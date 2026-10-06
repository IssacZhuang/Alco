namespace Alco.Graphics;

/// <summary>
/// The creation information for a render bundle. It currently only carries a
/// diagnostic name.
/// </summary>
public struct RenderBundleDescriptor
{
    /// <summary>
    /// Initializes the descriptor with a name.
    /// </summary>
    public RenderBundleDescriptor(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; }
}