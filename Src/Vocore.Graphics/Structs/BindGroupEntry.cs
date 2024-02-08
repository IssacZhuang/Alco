namespace Vocore.Graphics;

public struct BindGroupEntry
{
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

    public uint Binding { get; init; }
    public ShaderStage Stage { get; init; }
    public BindingType Type { get; init; }
    //Available when type is Texture
    public TextureBindingInfo TextureInfo { get; init; }
    //Available when type is StorageTexture
    public StorageTextureBindingInfo StorageTextureInfo { get; init; }
    public string Name { get; init; } = "unnamed_binding";

}