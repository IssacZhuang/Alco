namespace Vocore.Graphics;

public struct BindGroupDescriptor
{
    public string Name { get; init; }
    public BindGroupEntry[] Bindings { get; init; }
}