namespace Alco.Graphics;

/// <summary>
/// The location of a settable shader resource inside the bind groups of a shader.
/// <br/>A resource is a shader variable the material API can bind by name or id
/// (a buffer or a texture). Companion entries such as samplers and structured
/// buffer counters are not resources of their own; they are resolved from the
/// resource that owns them.
/// </summary>
public readonly struct ShaderResourceLocation
{
    /// <summary>The index of the bind group inside <see cref="ShaderReflectionInfo.BindGroups"/>.</summary>
    public int GroupIndex { get; init; }
    /// <summary>The index of the primary entry inside the bind group's bindings.</summary>
    public int EntryIndex { get; init; }
    /// <summary>The binding number of the primary entry.</summary>
    public uint Binding { get; init; }
    /// <summary>The binding type of the primary entry.</summary>
    public BindingType Type { get; init; }
    /// <summary>The shader name of the resource.</summary>
    public string Name { get; init; }

    public override string ToString()
    {
        return $"ShaderResourceLocation: {Name} (GroupIndex: {GroupIndex}, Binding: {Binding}, Type: {Type})";
    }
}
