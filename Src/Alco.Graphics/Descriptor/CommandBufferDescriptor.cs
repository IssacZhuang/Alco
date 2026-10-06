namespace Alco.Graphics;

/// <summary>
/// The creation information for a command buffer. It currently only carries a
/// diagnostic name.
/// </summary>
public struct CommandBufferDescriptor
{
    /// <summary>
    /// Initializes the descriptor with a name.
    /// </summary>
    public CommandBufferDescriptor(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; }
}