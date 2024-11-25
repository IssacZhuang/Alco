namespace Vocore.Graphics;

/// <summary>
/// Represents the creation information for a GPU buffer.
/// </summary>
public struct BufferDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BufferDescriptor"/> struct.
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
    /// The size of the buffer.
    /// </summary>
    public uint Size { get; init; }


    /// <summary>
    /// The usage flags for the buffer.
    /// </summary>
    public BufferUsage Usage { get; init; } = BufferUsage.MapRead | BufferUsage.MapWrite;

    /// <summary>
    /// The name of the buffer.
    /// </summary>
    public string Name { get; init; } = "unnamed_gpu_buffer";
}