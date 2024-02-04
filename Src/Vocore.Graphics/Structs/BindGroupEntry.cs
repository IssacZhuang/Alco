namespace Vocore.Graphics;

public struct BindGroupEntry
{
    public uint Binding { get; init; }
    public ShaderStage Stage { get; init; }
    public BindingType Type { get; init; }
    //Available when type is Texture
    public TextureBindingInfo TextureInfo { get; init; }
    //Available when type is StorageTexture
    public StorageTextureBindingInfo StorageTextureInfo { get; init; }

}