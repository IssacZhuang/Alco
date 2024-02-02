namespace Vocore.Graphics;

public struct VertexElement
{
    public VertexElement(uint location, uint offset, VertexFormat format, string name)
    {
        Location = location;
        Offset = offset;
        Format = format;
        Name = name;
    }
    public uint Location { get; init; }
    public uint Offset { get; init; }
    public VertexFormat Format { get; init; }
    public string Name { get; init; } = "Unnamed Vertex Element";
}