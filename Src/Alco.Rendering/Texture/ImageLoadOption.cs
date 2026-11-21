using Alco.Graphics;
using Alco;

namespace Alco.Rendering;

/// <summary>
/// Configuration options for image loading operations.
/// </summary>
public struct ImageLoadOption
{
    /// <summary>Default texture usage flags for loaded 2D images.</summary>
    public const TextureUsage DefaultUsage =
        TextureUsage.Sampled | TextureUsage.TransferDestination | TextureUsage.TransferSource;

    /// <summary>
    /// Default image load options with common settings.
    /// </summary>
    public static readonly ImageLoadOption Default = new ImageLoadOption
    {
        Format = Format.Rgba8Unorm,
        Usage = DefaultUsage,
        MipLevels = 1,
        FilterMode = Filter.Linear,
        AddressMode = AddressMode.Repeat,
        SlicePadding = Padding.Zero,
        PremultiplyAlpha = false,
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
    /// <param name="slicePadding">The slice padding for 9-slice textures.</param>
    /// <param name="name">The name of the texture for debugging.</param>
    public ImageLoadOption(
        Format format = Format.Rgba8Unorm,
        TextureUsage usage = DefaultUsage,
        uint mipLevels = 1,
        Filter filterMode = Filter.Linear,
        AddressMode addressMode = AddressMode.Repeat,
        Padding slicePadding = default,
        string name = "unnamed_texture"
    )
    {
        Format = format;
        MipLevels = mipLevels;
        Usage = usage;
        FilterMode = filterMode;
        AddressMode = addressMode;
        SlicePadding = slicePadding;
        Name = name;
    }

    /// <summary>
    /// The pixel format of the texture.
    /// </summary>
    public Format Format { get; init; } = Format.Rgba8Unorm;

    /// <summary>
    /// The number of mipmap levels.
    /// </summary>
    public uint MipLevels { get; init; } = 1;

    /// <summary>
    /// The usage flags for the texture.
    /// </summary>
    public TextureUsage Usage { get; init; } = DefaultUsage;

    /// <summary>
    /// The texture filtering mode.
    /// </summary>
    public Filter FilterMode { get; init; } = Filter.Linear;

    /// <summary>
    /// The maximum anisotropy for texture sampling; 1 disables anisotropic filtering.
    /// Values above 1 reduce minification blur and moiré on surfaces seen at grazing
    /// angles; clamped to the GPU limit (16) at sampler creation.
    /// </summary>
    public ushort Anisotropy { get; init; } = 1;

    /// <summary>
    /// The texture addressing mode.
    /// </summary>
    public AddressMode AddressMode { get; init; } = AddressMode.Repeat;

    /// <summary>
    /// The slice padding for 9-slice textures. Defines the padding for left, top, right, and bottom edges.
    /// </summary>
    public Padding SlicePadding { get; init; } = Padding.Zero;

    /// <summary>
    /// The name of the texture for debugging purposes.
    /// </summary>
    public string Name { get; init; } = "unnamed_texture";

    /// <summary>
    /// Whether to convert the image from straight alpha to premultiplied alpha during loading.
    /// </summary>
    public bool PremultiplyAlpha { get; init; } = false;
}