using Alco.Graphics;
using Alco.Graphics.LowLevel;
using static Alco.MemoryUtility;

namespace Alco.Rendering;

/// <summary>
/// GPU <see cref="Texture"/> creation, data upload, bitmap transfer, and PNG readback utilities built
/// over <see cref="RenderingSystem"/>.
/// </summary>
public static unsafe class TextureUtility
{
    /// <summary>Returns whether the device supports BC-family block-compressed textures.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <returns><c>true</c> when BC textures are supported; otherwise, <c>false</c>.</returns>
    public static bool IsBcSupported(RenderingSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return system.Device.Caps.TextureCompressionBc;
    }

    /// <summary>Creates an empty 2D GPU texture without uploading initial texels.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="format">The pixel format of the texture.</param>
    /// <param name="usage">The texture usage flags.</param>
    /// <param name="mipLevels">The number of mip levels.</param>
    /// <returns>A newly created <see cref="Texture"/>.</returns>
    public static Texture CreateTexture(
        RenderingSystem system,
        uint width,
        uint height,
        Format format = Format.Rgba8Unorm,
        TextureUsage usage = ImageLoadOption.DefaultUsage,
        uint mipLevels = 1)
    {
        ArgumentNullException.ThrowIfNull(system);

        return system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D(width, height, 1),
            MipLevels = mipLevels,
            LayerCount = 1,
            Format = format,
            Usage = usage,
            SampleCount = 1,
        });
    }

    /// <summary>Creates a solid-color 2D GPU texture.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="color">The fill color.</param>
    /// <param name="format">The pixel format of the texture; must match 4-byte RGBA layout.</param>
    /// <param name="usage">The texture usage flags.</param>
    /// <returns>A newly created <see cref="Texture"/> filled with <paramref name="color"/>.</returns>
    public static Texture CreateTexture(
        RenderingSystem system,
        uint width,
        uint height,
        Color32 color,
        Format format = Format.Rgba8Unorm,
        TextureUsage usage = ImageLoadOption.DefaultUsage)
    {
        ArgumentNullException.ThrowIfNull(system);

        int pixelCount = checked((int)(width * height));
        Color32* data = Alloc<Color32>(pixelCount);
        try
        {
            Memset(data, pixelCount, color);
            return CreateTexture(
                system,
                new ReadOnlySpan<byte>(data, checked(sizeof(Color32) * pixelCount)),
                width,
                height,
                format,
                usage);
        }
        finally
        {
            Free(data);
        }
    }

    /// <summary>Creates a 2D GPU texture initialized from raw texel bytes.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="data">The raw texel bytes matching <paramref name="format"/>.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="format">The pixel format of the texture.</param>
    /// <param name="usage">The texture usage flags.</param>
    /// <param name="mipLevels">The number of mip levels.</param>
    /// <returns>A newly created <see cref="Texture"/>.</returns>
    public static Texture CreateTexture(
        RenderingSystem system,
        ReadOnlySpan<byte> data,
        uint width,
        uint height,
        Format format = Format.Rgba8Unorm,
        TextureUsage usage = ImageLoadOption.DefaultUsage,
        uint mipLevels = 1)
    {
        Texture texture = CreateTexture(system, width, height, format, usage, mipLevels);
        try
        {
            if (!data.IsEmpty)
            {
                UploadTextureData(system, texture, data);
            }
            return texture;
        }
        catch
        {
            if (!texture.IsDestroyed)
            {
                texture.Destroy();
            }
            throw;
        }
    }

    /// <summary>Creates a 2D GPU texture initialized from an unmanaged texel buffer.</summary>
    /// <param name="system">The graphics rendering system.</param>
    /// <param name="data">Pointer to the raw texel bytes.</param>
    /// <param name="size">The buffer length in bytes.</param>
    /// <param name="width">The texture width in texels.</param>
    /// <param name="height">The texture height in texels.</param>
    /// <param name="format">The pixel format of the texture.</param>
    /// <param name="usage">The texture usage flags.</param>
    /// <param name="mipLevels">The number of mip levels.</param>
    /// <returns>A newly created <see cref="Texture"/>.</returns>
    public static Texture CreateTexture(
        RenderingSystem system,
        byte* data,
        uint size,
        uint width,
        uint height,
        Format format = Format.Rgba8Unorm,
        TextureUsage usage = ImageLoadOption.DefaultUsage,
        uint mipLevels = 1)
    {
        ReadOnlySpan<byte> span = data == null || size == 0
            ? ReadOnlySpan<byte>.Empty
            : new ReadOnlySpan<byte>(data, checked((int)size));
        return CreateTexture(system, span, width, height, format, usage, mipLevels);
    }

    /// <summary>Uploads raw pixel bytes into a GPU <see cref="Texture"/>.</summary>
    /// <param name="system">The graphics rendering system owning <paramref name="target"/>.</param>
    /// <param name="target">The destination GPU texture created with <see cref="TextureUsage.TransferDestination"/>.</param>
    /// <param name="data">The source texel bytes.</param>
    /// <param name="copy">Optional region and layout description for the upload.</param>
    public static void UploadTextureData(
        RenderingSystem system,
        Texture target,
        ReadOnlySpan<byte> data,
        in TextureCopyDesc copy = default)
    {
        ArgumentNullException.ThrowIfNull(system);
        system.UploadTexture(target, data, copy);
    }

    /// <summary>Uploads a full CPU bitmap into a GPU <see cref="Texture"/>.</summary>
    /// <typeparam name="T">The unmanaged pixel element type matching the texture format.</typeparam>
    /// <param name="system">The graphics rendering system owning <paramref name="target"/>.</param>
    /// <param name="target">The destination GPU texture created with <see cref="TextureUsage.TransferDestination"/>.</param>
    /// <param name="bitmap">The source bitmap whose dimensions match <paramref name="target"/>.</param>
    public static void UploadBitmap<T>(RenderingSystem system, Texture target, Bitmap<T> bitmap) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bitmap);

        if (bitmap.Width != target.Extent.X || bitmap.Height != target.Extent.Y)
        {
            throw new ArgumentException("The size of the bitmap does not match the size of the texture.");
        }

        UploadTextureData(
            system,
            target,
            new ReadOnlySpan<byte>(bitmap.UnsafePointer, checked(bitmap.Width * bitmap.Height * sizeof(T))));
    }

    /// <summary>Uploads a sub-rectangle of a CPU bitmap into a GPU <see cref="Texture"/>.</summary>
    /// <typeparam name="T">The unmanaged pixel element type matching the texture format.</typeparam>
    /// <param name="system">The graphics rendering system owning <paramref name="target"/>.</param>
    /// <param name="target">The destination GPU texture created with <see cref="TextureUsage.TransferDestination"/>.</param>
    /// <param name="bitmap">The source CPU bitmap containing the region.</param>
    /// <param name="x">The horizontal origin of the region in texels.</param>
    /// <param name="y">The vertical origin of the region in texels.</param>
    /// <param name="width">The width of the region in texels.</param>
    /// <param name="height">The height of the region in texels.</param>
    public static void UploadBitmap<T>(
        RenderingSystem system,
        Texture target,
        Bitmap<T> bitmap,
        int x,
        int y,
        int width,
        int height) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        uint texWidth = target.Extent.X;
        uint texHeight = target.Extent.Y;
        if (x + width > texWidth || y + height > texHeight || x + width > bitmap.Width || y + height > bitmap.Height)
        {
            throw new ArgumentOutOfRangeException(
                $"The region ({x}, {y}, {width}, {height}) exceeds the texture size ({texWidth}, {texHeight}) or bitmap size ({bitmap.Width}, {bitmap.Height}).");
        }
        if (width == 0 || height == 0)
        {
            return;
        }

        int pixelSize = sizeof(T);
        long srcRowBytes = (long)bitmap.Width * pixelSize;
        byte* start = (byte*)bitmap.UnsafePointer + (long)y * srcRowBytes + (long)x * pixelSize;
        int regionSliceLength = checked((int)((long)(height - 1) * srcRowBytes + (long)width * pixelSize));

        UploadTextureData(
            system,
            target,
            new ReadOnlySpan<byte>(start, regionSliceLength),
            new TextureCopyDesc
            {
                Offset = new Extent3D((uint)x, (uint)y, 0),
                Extent = new Extent3D((uint)width, (uint)height, 1),
                RowPitchBytes = (ulong)srcRowBytes,
            });
    }

    /// <summary>Reads back an RGBA8 GPU <see cref="Texture"/> and writes it as a PNG stream.</summary>
    /// <param name="system">The graphics rendering system owning <paramref name="texture"/>.</param>
    /// <param name="texture">The RGBA8 texture created with <see cref="TextureUsage.TransferSource"/>.</param>
    /// <param name="output">The writable destination stream, which is left open.</param>
    public static void EncodeTextureToPNG(RenderingSystem system, Texture texture, Stream output)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(output);
        if (texture.IsDestroyed)
        {
            throw new ObjectDisposedException(nameof(Texture), "Cannot encode a destroyed texture.");
        }

        Format format = texture.Format;
        if (format != Format.Rgba8Unorm && format != Format.Rgba8Srgb)
        {
            throw new ArgumentException(
                $"Texture format {format} is not supported. Only Rgba8Unorm and Rgba8Srgb are supported.",
                nameof(texture));
        }

        if ((texture.Desc.Usage & TextureUsage.TransferSource) == 0)
        {
            throw new InvalidOperationException("Texture must be created with TextureUsage.TransferSource for PNG encoding.");
        }

        int width = checked((int)texture.Extent.X);
        int height = checked((int)texture.Extent.Y);
        int dataSize = checked(width * height * 4);
        byte* pixels = Alloc<byte>(dataSize);
        try
        {
            system.ReadTexture(texture, new Span<byte>(pixels, dataSize));
            ImageEncodeUtility.WritePng(new ReadOnlySpan<byte>(pixels, dataSize), width, height, output);
        }
        finally
        {
            Free(pixels);
        }
    }

    /// <summary>Gets the byte size per compressed 4x4 block for a block-compressed pixel format.</summary>
    /// <param name="format">The pixel format to inspect.</param>
    /// <param name="blockBytes">When this method returns <c>true</c>, the bytes per compressed block; otherwise, <c>0</c>.</param>
    /// <returns><c>true</c> when <paramref name="format"/> is block-compressed; otherwise, <c>false</c>.</returns>
    public static bool TryGetBlockBytes(Format format, out uint blockBytes)
    {
        TextureFormatInfo info = Gpu.GetTextureFormatInfo(format);
        if (info.BlockExtent.X <= 1 && info.BlockExtent.Y <= 1)
        {
            blockBytes = 0;
            return false;
        }

        blockBytes = info.BytesPerBlock;
        return blockBytes > 0;
    }

    /// <summary>Returns whether <paramref name="format"/> uses an sRGB transfer function.</summary>
    /// <param name="format">The pixel format to inspect.</param>
    /// <returns><c>true</c> when the format is sRGB; otherwise, <c>false</c>.</returns>
    public static bool IsSrgbFormat(Format format) => format switch
    {
        Format.R8Srgb or
        Format.Rg8Srgb or
        Format.Rgba8Srgb or
        Format.Bgra8Srgb or
        Format.Astc4x4Srgb or
        Format.Bc1Srgb or
        Format.Bc2Srgb or
        Format.Bc3Srgb or
        Format.Bc7Srgb => true,
        _ => false,
    };
}
