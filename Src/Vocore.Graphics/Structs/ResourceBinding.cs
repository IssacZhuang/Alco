namespace Vocore.Graphics;

public struct ResourceBinding
{
    public uint Binding { get; init; }
    public string Name { get; init; }
    public BindingType Type { get; init; }
    //Available when type is Texture
    public TextureBindingInfo TextureInfo { get; init; }
    //Available when type is StorageTexture
    public StorageTextureBindingInfo StorageTextureInfo { get; init; }

}