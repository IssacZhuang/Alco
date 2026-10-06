namespace Alco.Graphics;

/// <summary>
/// Represents the creation information for a GPU buffer.
/// </summary>
public struct BufferDescriptor
{
    /// <summary>
    /// Initializes the descriptor with a size, usage and diagnostic name.
    /// </summary>
    /// <param name="size">The size of the buffer.</param>
    /// <param name="usage">The usage flags for the buffer.</param>
    /// <param name="name">The name of the buffer (optional).</param>
    public BufferDescriptor(uint size, BufferUsage usage, string name = "unnamed_gpu_buffer")
    {
        Size = size;
        Usage = usage;
        Name = name;
    }

    /// <summary>
    /// Size in bytes.
    /// </summary>
    public uint Size { get; init; }


    /// <summary>
    /// How the buffer will be used (copy source/destination, vertex/index/uniform/storage).
    /// </summary>
    public BufferUsage Usage { get; init; } = BufferUsage.MapRead | BufferUsage.MapWrite;

    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed_gpu_buffer";
}