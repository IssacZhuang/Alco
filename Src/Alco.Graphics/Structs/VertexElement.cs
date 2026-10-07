namespace Alco.Graphics;

/// <summary>
/// One vertex attribute of a vertex input layout: where it lives in the vertex
/// data and how it is fed to a shader input.
/// </summary>
public struct VertexElement
{
    /// <summary>
    /// Initializes the element with its shader location, byte offset and format.
    /// </summary>
    public VertexElement(uint location, uint offset, VertexFormat format, string name)
    {
        Location = location;
        Offset = offset;
        Format = format;
        Name = name;
    }
    /// <summary>
    /// The shader input location this attribute feeds.
    /// </summary>
    public uint Location { get; init; }
    /// <summary>
    /// Byte offset of the attribute within one vertex.
    /// </summary>
    public uint Offset { get; init; }
    /// <summary>
    /// The memory layout of the attribute's components.
    /// </summary>
    public VertexFormat Format { get; init; }
    /// <summary>
    /// Diagnostic name of the attribute.
    /// </summary>
    public string Name { get; init; } = "unnamed_vertex_element";

    public override string ToString()
    {
        return $"Location: {Location}, Name: {Name}, Offset: {Offset}, Format: {Format} ";
    }
}