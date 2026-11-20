using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

public static unsafe partial class Gpu
{
    // Synchronous diagnostics on the calling thread. Do not call graphics APIs recursively.
    // The thread-local callback defaults to null. Fatal execution errors still abort after the callback returns.
    [ThreadStatic] internal static ErrorCallback? ErrorCallback;

    /// <summary>Installs a thread-local diagnostic receiver.</summary>
    /// <param name="callback">The receiver, or null to clear it. The receiver must not throw or call graphics APIs recursively.</param>
    public static void SetErrorCallback(ErrorCallback? callback) => ErrorCallback = callback;

    internal static void ReportError(string message, bool fatal)
    {
        ErrorCallback?.Invoke(message, fatal);
        if (ErrorCallback != null) return;
        if (fatal)
        {
            Console.Error.WriteLine(message);
            System.Diagnostics.Debug.Assert(false, message);
        }
    }

    /// <summary>Returns the texel-block geometry of a concrete format; the default result covers <see cref="Format.Undefined"/>.</summary>
    /// <param name="format">The texture format.</param>
    /// <returns>The block extent, byte size, and depth/stencil aspects.</returns>
    public static TextureFormatInfo GetTextureFormatInfo(Format format) => format switch
    {
        Format.R8Srgb or Format.R8Unorm or Format.R8Uint or Format.S8Uint => new TextureFormatInfo
        {
            BlockExtent = new Extent2D(1, 1),
            BytesPerBlock = 1,
            Depth = false,
            Stencil = format == Format.S8Uint,
        },
        Format.Rg8Srgb or Format.Rgba4Unorm or Format.R5g5b5a1Unorm or Format.R5g6b5Unorm or
        Format.Rg8Unorm or Format.R16Unorm or Format.Rg8Uint or Format.R16Uint or Format.R16Float or
        Format.D16Unorm => new TextureFormatInfo
        {
            BlockExtent = new Extent2D(1, 1),
            BytesPerBlock = 2,
            Depth = format == Format.D16Unorm,
            Stencil = false,
        },
        Format.Rgba8Srgb or Format.Bgra8Srgb or Format.Rgba8Unorm or Format.Bgra8Unorm or Format.Rg16Unorm or
        Format.Rgba8Uint or Format.Bgra8Uint or Format.Rg16Uint or Format.R32Uint or Format.Rg16Float or
        Format.R32Float or Format.Rgb10a2Unorm or Format.Rg11b10Float or Format.D24UnormS8Uint or
        Format.D32Float => new TextureFormatInfo
        {
            BlockExtent = new Extent2D(1, 1),
            BytesPerBlock = 4,
            Depth = format == Format.D24UnormS8Uint || format == Format.D32Float,
            Stencil = format == Format.D24UnormS8Uint,
        },
        Format.Rgba16Unorm or Format.Rgba16Uint or Format.Rg32Uint or Format.Rgba16Float or
        Format.Rg32Float or Format.D32FloatS8Uint => new TextureFormatInfo
        {
            BlockExtent = new Extent2D(1, 1),
            BytesPerBlock = 8,
            Depth = format == Format.D32FloatS8Uint,
            Stencil = format == Format.D32FloatS8Uint,
        },
        Format.Rgba32Uint or Format.Rgba32Float => new TextureFormatInfo
        {
            BlockExtent = new Extent2D(1, 1),
            BytesPerBlock = 16,
        },
        Format.Bc1Srgb or Format.Bc1Unorm or Format.Bc4Unorm or Format.Bc4Snorm => new TextureFormatInfo
        {
            BlockExtent = new Extent2D(4, 4),
            BytesPerBlock = 8,
        },
        Format.EacRg or Format.Astc4x4Srgb or Format.Astc4x4Unorm or Format.Bc2Srgb or Format.Bc2Unorm or
        Format.Bc3Srgb or Format.Bc3Unorm or Format.Bc5Rg or Format.Bc6HUfloat or Format.Bc6HSfloat or
        Format.Bc7Srgb or Format.Bc7Unorm => new TextureFormatInfo
        {
            BlockExtent = new Extent2D(4, 4),
            BytesPerBlock = 16,
        },
        _ => default,
    };
}
