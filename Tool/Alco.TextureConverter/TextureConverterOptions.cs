using Alco.Graphics;

namespace Alco.TextureConverter;

/// <summary>
/// The block-compression format the converter produces.
/// </summary>
public enum TextureConverterFormat
{
    /// <summary>BC1 (DXT1): opaque RGB + 1-bit punch-through alpha at 4 bits per pixel.</summary>
    Bc1,

    /// <summary>BC2 (DXT3): RGB plus explicit 4-bit alpha at 8 bits per pixel.</summary>
    Bc2,

    /// <summary>BC3 (DXT5): RGB plus interpolated 8-bit alpha at 8 bits per pixel.</summary>
    Bc3,

    /// <summary>BC4 UNORM: single normalized unsigned channel at 4 bits per pixel.</summary>
    Bc4,

    /// <summary>BC4 SNORM: single normalized signed channel at 4 bits per pixel.</summary>
    Bc4Snorm,

    /// <summary>BC5 UNORM: two normalized unsigned channels at 8 bits per pixel.</summary>
    Bc5,

    /// <summary>BC7 (BPTC): high-quality RGBA block compression at 8 bits per pixel.</summary>
    Bc7,
}

/// <summary>
/// Options controlling one batch PNG-to-DDS conversion run.
/// </summary>
/// <param name="InputPath">A PNG file, or a directory converted recursively.</param>
/// <param name="Format">The block-compression format to encode.</param>
/// <param name="InPlace">Replace source PNG files with DDS files; mutually exclusive with <paramref name="OutputPath"/>.</param>
/// <param name="NoMips">Encode level 0 only without generating a mip chain.</param>
/// <param name="OutputPath">Mirror the input directory structure under this root (default writes next to sources).</param>
/// <param name="Verify">Decode level 0 back on the GPU and measure PSNR / maximum error against the source.</param>
/// <param name="Srgb">Encode sRGB DX10 DDS format variants for BC1/BC2/BC3/BC7 textures.</param>
/// <param name="PremultiplyAlpha">Override premultiplied-alpha preprocessing; null reads <c>.texture-option.meta</c> inheritance.</param>
public sealed record TextureConverterOptions(
    string InputPath,
    TextureConverterFormat Format = TextureConverterFormat.Bc7,
    bool InPlace = false,
    bool NoMips = false,
    string? OutputPath = null,
    bool Verify = false,
    bool Srgb = false,
    bool? PremultiplyAlpha = null)
{
    /// <summary>Validates path and mutual-exclusion constraints before opening a GPU device.</summary>
    /// <param name="error">The validation failure message, or null when valid.</param>
    /// <returns><c>true</c> when the option combination is valid.</returns>
    public bool Validate(out string? error)
    {
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            error = "The input path is empty.";
            return false;
        }
        if (InPlace && OutputPath != null)
        {
            error = "InPlace and OutputPath are mutually exclusive.";
            return false;
        }
        error = null;
        return true;
    }
}
