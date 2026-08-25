using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Header-level description of an image file, obtained by probing the file header
/// without decoding pixels (see <see cref="ImageDecodeUtility.GetImageFileInfo"/>).
/// Used by texture streaming to create the GPU texture at its final specification up
/// front so content can be uploaded in place later without any identity change.
/// </summary>
public readonly struct ImageFileInfo
{
    /// <summary>Level-0 width in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Level-0 height in pixels.</summary>
    public int Height { get; init; }

    /// <summary>
    /// Whether the file holds block-compressed data (DDS, BC1-BC7). When true,
    /// <see cref="Format"/>, <see cref="MipLevels"/> and <see cref="DataOffset"/> are
    /// dictated by the file and override the corresponding load options; the payload is
    /// uploaded verbatim without pixel decoding.
    /// </summary>
    public bool IsBlockCompressed { get; init; }

    /// <summary>
    /// The engine pixel format dictated by the file. Only meaningful when
    /// <see cref="IsBlockCompressed"/> is true; already resolved to the sRGB variant
    /// requested by the probing caller.
    /// </summary>
    public PixelFormat Format { get; init; }

    /// <summary>
    /// The number of mip levels dictated by the file. Only block-compressed files carry
    /// a mip chain (&gt;1); PNG/JPEG always report 1.
    /// </summary>
    public int MipLevels { get; init; }

    /// <summary>
    /// Byte offset of the mip chain inside the file. Only meaningful when
    /// <see cref="IsBlockCompressed"/> is true; 0 otherwise.
    /// </summary>
    public int DataOffset { get; init; }
}
