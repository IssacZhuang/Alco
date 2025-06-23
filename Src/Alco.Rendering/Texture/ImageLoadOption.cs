using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Configuration options for image loading operations.
/// </summary>
public struct ImageLoadOption
{
    /// <summary>
    /// Default image load options with common settings.
    /// </summary>
    public static readonly ImageLoadOption Default = new ImageLoadOption
    {
        Format = PixelFormat.RGBA8Unorm,
        Usage = TextureUsage.Standard,
        MipLevels = 1,
        FilterMode = FilterMode.Linear,
        AddressMode = AddressMode.ClampToEdge,
        Name = "unnamed_texture"
    };

    /// <summary>
    /// Initializes a new instance of ImageLoadOption.
    /// </summary>
    /// <param name="format">The pixel format of the texture.</param>
    /// <param name="usage">The usage flags for the texture.</param>
    /// <param name="mipLevels">The number of mipmap levels.</param>
    /// <param name="filterMode">The texture filtering mode.</param>
    /// <param name="addressMode">The texture addressing mode.</param>
    /// <param name="name">The name of the texture for debugging.</param>
    public ImageLoadOption(
        PixelFormat format = PixelFormat.RGBA8Unorm,
        TextureUsage usage = TextureUsage.Standard,
        uint mipLevels = 1,
        FilterMode filterMode = FilterMode.Linear,
        AddressMode addressMode = AddressMode.ClampToEdge,
        string name = "unnamed_texture"
    )
    {
        Format = format;
        MipLevels = mipLevels;
        Usage = usage;
        FilterMode = filterMode;
        AddressMode = addressMode;
        Name = name;
    }

    /// <summary>
    /// The pixel format of the texture.
    /// </summary>
    public PixelFormat Format { get; init; } = PixelFormat.RGBA8Unorm;

    /// <summary>
    /// The number of mipmap levels.
    /// </summary>
    public uint MipLevels { get; init; } = 1;

    /// <summary>
    /// The usage flags for the texture.
    /// </summary>
    public TextureUsage Usage { get; init; } = TextureUsage.Standard;

    /// <summary>
    /// The texture filtering mode.
    /// </summary>
    public FilterMode FilterMode { get; init; } = FilterMode.Linear;

    /// <summary>
    /// The texture addressing mode.
    /// </summary>
    public AddressMode AddressMode { get; init; } = AddressMode.ClampToEdge;

    /// <summary>
    /// The name of the texture for debugging purposes.
    /// </summary>
    public string Name { get; init; } = "unnamed_texture";
}