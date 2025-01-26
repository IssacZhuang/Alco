namespace Alco.Graphics;

public struct StorageTextureBindingInfo
{
    public StorageTextureBindingInfo(
        AccessMode access,
        TextureViewDimension viewDimension,
        PixelFormat format)
    {
        Access = access;
        ViewDimension = viewDimension;
        Format = format;
    }


    public AccessMode Access;
    public TextureViewDimension ViewDimension { get; init; }
    public PixelFormat Format { get; init; }

    public static readonly StorageTextureBindingInfo None = new()
    {
        Access = AccessMode.None,
        ViewDimension = TextureViewDimension.Undefined,
        Format = PixelFormat.Undefined
    };
}
