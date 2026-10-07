namespace Alco.Graphics;

/// <summary>
/// Declares how a storage texture binding is read and written by shaders.
/// </summary>
public struct StorageTextureBindingInfo
{
    /// <summary>
    /// Initializes the info with access mode, view dimension and format.
    /// </summary>
    public StorageTextureBindingInfo(
        AccessMode access,
        TextureViewDimension viewDimension,
        PixelFormat format)
    {
        Access = access;
        ViewDimension = viewDimension;
        Format = format;
    }


    /// <summary>
    /// Whether shaders read, write or read-write access the texels.
    /// </summary>
    public AccessMode Access;
    /// <summary>
    /// How the texture is dimensioned in shaders.
    /// </summary>
    public TextureViewDimension ViewDimension { get; init; }
    /// <summary>
    /// The texel format the shaders read and write.
    /// </summary>
    public PixelFormat Format { get; init; }

    /// <summary>
    /// Empty info marking "no storage texture"; used for non-storage bindings.
    /// </summary>
    public static readonly StorageTextureBindingInfo None = new()
    {
        Access = AccessMode.None,
        ViewDimension = TextureViewDimension.Undefined,
        Format = PixelFormat.Undefined
    };
}
