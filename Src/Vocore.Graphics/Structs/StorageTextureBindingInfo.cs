namespace Vocore.Graphics;

public struct StorageTextureBindingInfo
{
    public AccessMode access;
    public TextureViewDimension ViewDimension { get; init; }
    public PixelFormat Format { get; init; }
}
