namespace Vocore.Graphics;

/// <summary>
/// Represents the creation information for a GPU buffer.
/// </summary>
public struct BufferCreateInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BufferCreateInfo"/> struct.
    /// </summary>
    /// <param name="size">The size of the buffer.</param>
    /// <param name="usage">The usage flags for the buffer.</param>
    /// <param name="accessMode">The access mode for the buffer.</param>
    /// <param name="name">The name of the buffer (optional).</param>
    public BufferCreateInfo(ulong size, BufferUsage usage, AccessMode accessMode, string name = "Unnamed GPU buffer")
    {
        Size = size;
        AccessMode = accessMode;
        Usage = usage;
        Name = name;
    }

    /// <summary>
    /// Validates the GPU buffer creation information.
    /// </summary>
    public readonly void Validate()
    {
        UtilsAssert.IsTrue(Size > 0, "Buffer size must be greater than 0");
    }

    /// <summary>
    /// The size of the buffer.
    /// </summary>
    public required ulong Size { get; init; }

    /// <summary>
    /// The access mode for the buffer.
    /// </summary>
    public AccessMode AccessMode { get; init; } = AccessMode.None;

    /// <summary>
    /// The usage flags for the buffer.
    /// </summary>
    public BufferUsage Usage { get; init; } = BufferUsage.MapRead | BufferUsage.MapWrite;

    /// <summary>
    /// The name of the buffer.
    /// </summary>
    public string? Name { get; init; } = "Unnamed GPU buffer";
}