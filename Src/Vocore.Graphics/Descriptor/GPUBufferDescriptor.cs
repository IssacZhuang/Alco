namespace Vocore.Graphics;

public struct GPUBufferDescriptor
{
    public GPUBufferDescriptor(ulong size, BufferUsage usage, AccessMode accessMode, string name = "Unnamed GPU buffer")
    {
        UtilsAssert.IsTrue(size > 0, "Buffer size must be greater than 0");
        Size = size;
        AccessMode = accessMode;
        Usage = usage;
        Name = name;
    }
    public required ulong Size { get; init; }
    public AccessMode AccessMode { get; init; } = AccessMode.None;
    public BufferUsage Usage { get; init; } = BufferUsage.MapRead | BufferUsage.MapWrite;
    public string? Name { get; init; } = "Unnamed GPU buffer";
}