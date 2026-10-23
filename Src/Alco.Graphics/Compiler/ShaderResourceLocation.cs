namespace Alco.Graphics;

/// <summary>
/// The location of a settable shader resource (a buffer or a texture) inside a
/// shader's bind groups. Samplers and structured-buffer counters are not resources
/// of their own; they are resolved from the resource that owns them.
/// </summary>
public readonly struct ShaderResourceLocation
{
    /// <summary>The index of the bind group inside <see cref="ShaderReflection.BindGroups"/>.</summary>
    public int GroupIndex { get; init; }
    /// <summary>The index of the primary entry inside the bind group's bindings.</summary>
    public int EntryIndex { get; init; }
    /// <summary>The binding number of the primary entry.</summary>
    public uint Binding { get; init; }
    /// <summary>The binding type of the primary entry.</summary>
    public BindingType Type { get; init; }
    /// <summary>The shader name of the resource.</summary>
    public string Name { get; init; }

    /// <summary>Returns a diagnostic description of the resource location.</summary>
    public override string ToString()
    {
        return $"ShaderResourceLocation: {Name} (GroupIndex: {GroupIndex}, Binding: {Binding}, Type: {Type})";
    }
}
