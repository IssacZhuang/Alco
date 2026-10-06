namespace Alco.Graphics;

/// <summary>
/// Represents the creation information for a GPU texture.
/// </summary>
public struct TextureDescriptor
{
    /// <summary>
    /// Initializes the descriptor with dimension, format, size and usage.
    /// </summary>
    /// <param name="dimension">The dimension of the texture.</param>
    /// <param name="format">The pixel format of the texture.</param>
    /// <param name="width">The width of the texture.</param>
    /// <param name="height">The height of the texture.</param>
    /// <param name="depthOrArrayLayer">The depth or array layer of the texture.</param>
    /// <param name="mipLevels">The number of mip levels of the texture.</param>
    /// <param name="usage">The usage of the texture.</param>
    /// <param name="sampleCount">The sample count of the texture.</param>
    /// <param name="name">The name of the texture.</param>
    public TextureDescriptor(
        TextureDimension dimension,
        PixelFormat format,
        uint width,
        uint height,
        uint depthOrArrayLayer = 1,
        uint mipLevels = 1,
        TextureUsage usage = TextureUsage.TextureBinding | TextureUsage.Write,
        uint sampleCount = 1,
        string name = "Unnamed GPU texture")
    {
        Name = name;
        Format = format;
        Width = width;
        Height = height;
        DepthOrArrayLayer = depthOrArrayLayer;
        MipLevels = mipLevels;
        SampleCount = sampleCount;
        Dimension = dimension;
        Usage = usage;
    }

    /// <summary>
    /// Validates the GPU texture creation information.
    /// </summary>
    public readonly void Validate()
    {
        AssetUtility.IsTrue(Format != PixelFormat.Undefined, "Texture format must be defined");
        AssetUtility.IsTrue(Width > 0, "Texture width must be greater than 0");
        AssetUtility.IsTrue(Height > 0, "Texture height must be greater than 0");
        AssetUtility.IsTrue(DepthOrArrayLayer > 0, "Texture depth or array layer must be greater than 0");
        AssetUtility.IsTrue(MipLevels > 0, "Texture mip levels must be greater than 0");
    }

    /// <summary>
    /// The dimensionality of the texture (1D, 2D or 3D).
    /// </summary>
    public TextureDimension Dimension { get; set; } = TextureDimension.Texture2D;

    /// <summary>
    /// The pixel format of the texels.
    /// </summary>
    public PixelFormat Format { get; set; } = PixelFormat.RGBA8Unorm;

    /// <summary>
    /// How the texture may be used by the GPU.
    /// </summary>
    public TextureUsage Usage { get; set; } = TextureUsage.TextureBinding | TextureUsage.Write;

    /// <summary>
    /// The width of the texture in texels.
    /// </summary>
    public uint Width { get; set; } = 1;

    /// <summary>
    /// The height of the texture in texels.
    /// </summary>
    public uint Height { get; set; } = 1;

    /// <summary>
    /// Depth for 3D textures; array layer count otherwise.
    /// </summary>
    public uint DepthOrArrayLayer { get; set; } = 1;

    /// <summary>
    /// The number of mip levels; must be at least 1 (a full mip chain is not
    /// created implicitly).
    /// </summary>
    public uint MipLevels { get; set; } = 1;

    /// <summary>
    /// MSAA sample count; 1 = no multisampling.
    /// </summary>
    public uint SampleCount { get; set; } = 1;

    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; set; } = "Unnamed GPU texture";
}