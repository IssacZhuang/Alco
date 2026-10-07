namespace Alco.Graphics;

/// <summary>
/// How a buffer may be used by the GPU. Usage must be declared when the buffer
/// is created; the backend validates every later operation against these bits.
/// </summary>
[Flags]
public enum BufferUsage
{
    /// <summary>No usage; the buffer cannot be used in any GPU operation.</summary>
    None = 0,
    /// <summary>The buffer can be mapped for CPU reads.</summary>
    MapRead = 1 << 0,
    /// <summary>The buffer can be mapped for CPU writes.</summary>
    MapWrite = 1 << 1,
    /// <summary>The buffer can be the source of a copy command.</summary>
    CopySrc = 1 << 2,
    /// <summary>The buffer can be the destination of a copy or clear.</summary>
    CopyDst = 1 << 3,
    /// <summary>The buffer can be bound as an index buffer.</summary>
    Index = 1 << 4,
    /// <summary>The buffer can be bound as a vertex buffer.</summary>
    Vertex = 1 << 5,
    /// <summary>The buffer can be bound as a uniform buffer.</summary>
    Uniform = 1 << 6,
    /// <summary>The buffer can be bound as a storage buffer.</summary>
    Storage = 1 << 7,
    /// <summary>The buffer can hold the argument records of indirect draw/dispatch commands.</summary>
    Indirect = 1 << 8,
    /// <summary>The buffer can receive timestamp query results.</summary>
    QueryResolve = 1 << 9,
}