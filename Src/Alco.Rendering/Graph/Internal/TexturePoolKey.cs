using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The identity key of a pooled texture: two requests with equal keys may share the
/// same underlying GPU texture. Value type with value equality, usable as a
/// dictionary key without allocation.
/// </summary>
internal readonly struct TexturePoolKey : System.IEquatable<TexturePoolKey>
{
    /// <summary>The texture width in pixels.</summary>
    internal readonly uint Width;

    /// <summary>The texture height in pixels.</summary>
    internal readonly uint Height;

    /// <summary>The pixel format.</summary>
    internal readonly PixelFormat Format;

    /// <summary>The texture usage flags.</summary>
    internal readonly TextureUsage Usage;

    /// <summary>The number of mip levels.</summary>
    internal readonly uint MipLevels;

    internal TexturePoolKey(uint width, uint height, PixelFormat format, TextureUsage usage, uint mipLevels = 1)
    {
        Width = width;
        Height = height;
        Format = format;
        Usage = usage;
        MipLevels = mipLevels;
    }

    /// <inheritdoc />
    public bool Equals(TexturePoolKey other)
    {
        return Width == other.Width
            && Height == other.Height
            && Format == other.Format
            && Usage == other.Usage
            && MipLevels == other.MipLevels;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TexturePoolKey other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (int)Width;
            hash = hash * 31 + (int)Height;
            hash = hash * 31 + (int)Format;
            hash = hash * 31 + (int)Usage;
            hash = hash * 31 + (int)MipLevels;
            return hash;
        }
    }
}
