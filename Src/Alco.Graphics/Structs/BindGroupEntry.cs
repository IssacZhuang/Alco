namespace Alco.Graphics;

/// <summary>
/// Declares one binding slot of a bind group layout: its binding number, shader
/// stage visibility and resource type.
/// </summary>
public struct BindGroupEntry
{
    /// <summary>
    /// Initializes the entry with its binding number, stage visibility and type.
    /// Texture entries default to a 2D float-sampled texture; storage texture
    /// entries default to no storage info.
    /// </summary>
    public BindGroupEntry(
        uint binding,
        ShaderStage stage,
        BindingType type,
        TextureBindingInfo? textureInfo = null,
        StorageTextureBindingInfo? storageTextureInfo = null,
        string name = "unnamed_binding"
        )
    {
        Binding = binding;
        Stage = stage;
        Type = type;
        Name = name;

        if (textureInfo.HasValue)
        {
            TextureInfo = textureInfo.Value;
        }
        else if (type == BindingType.Texture)
        {
            TextureInfo = TextureBindingInfo.Default2D;
        }
        else
        {
            TextureInfo = TextureBindingInfo.None;
        }

        if (storageTextureInfo.HasValue)
        {
            StorageTextureInfo = storageTextureInfo.Value;
        }
        else
        {
            StorageTextureInfo = StorageTextureBindingInfo.None;
        }
    }

    /// <summary>
    /// The binding number this slot is addressed by in shaders.
    /// </summary>
    public uint Binding { get; init; }
    /// <summary>
    /// The shader stages that can access this binding.
    /// </summary>
    public ShaderStage Stage { get; set; }
    /// <summary>
    /// The kind of resource the slot declares.
    /// </summary>
    public BindingType Type { get; init; }
    /// <summary>
    /// Sampled-texture details; only meaningful when <see cref="Type"/> is
    /// <see cref="BindingType.Texture"/>, otherwise <see cref="TextureBindingInfo.None"/>.
    /// </summary>
    public TextureBindingInfo TextureInfo { get; init; }
    /// <summary>
    /// Storage-texture details; only meaningful when <see cref="Type"/> is
    /// <see cref="BindingType.StorageTexture"/>, otherwise
    /// <see cref="StorageTextureBindingInfo.None"/>.
    /// </summary>
    public StorageTextureBindingInfo StorageTextureInfo { get; init; }
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed_binding";

    public override string ToString()
    {
        return $"BindGroupEntry: {Name} (Binding: {Binding}, Stage: {Stage}, Type: {Type})";
    }

}