namespace Vocore.Graphics;

public struct ResourceBindingLayout
{
    public uint Set { get; init; }
    public string Name { get; init; }
    public ResourceBinding[] Bindings { get; init; }
}