namespace Vocore.Graphics;

public struct BindGroupDescriptor
{
    public BindGroupDescriptor(
        BindGroupEntry[] bindings,
        string name = "unnamed_bind_group")
    {
        Name = name;
        Bindings = bindings;
    }

    public string Name { get; init; } = "unnamed_bind_group";
    public BindGroupEntry[] Bindings { get; init; }
}