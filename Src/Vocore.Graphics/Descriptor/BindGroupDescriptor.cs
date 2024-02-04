namespace Vocore.Graphics;

public struct BindGroupDescriptor
{
    public uint Set { get; init; }
    public string Name { get; init; }
    public BindGroupEntry[] Bindings { get; init; }
}