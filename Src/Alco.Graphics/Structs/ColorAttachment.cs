using System.Numerics;

namespace Alco.Graphics;

/// <summary>
/// Declares one color attachment of a frame buffer layout: its format and the
/// color the attachment is cleared to.
/// </summary>
public struct ColorAttachment
{
    /// <summary>
    /// Initializes the attachment with a format and the default clear color.
    /// </summary>
    public ColorAttachment(PixelFormat format)
    {
        Format = format;
    }

    /// <summary>
    /// Initializes the attachment with a format and an explicit clear color.
    /// </summary>
    public ColorAttachment(PixelFormat format, Vector4 clearColor)
    {
        Format = format;
        ClearColor = clearColor;
    }

    /// <summary>
    /// The texel format of the attachment.
    /// </summary>
    public PixelFormat Format { get; init; }
    /// <summary>
    /// The RGBA value the attachment is cleared to at pass start.
    /// </summary>
    public Vector4 ClearColor { get; init; } = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

    public static bool operator ==(ColorAttachment left, ColorAttachment right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ColorAttachment left, ColorAttachment right)
    {
        return !(left == right);
    }

    public readonly override bool Equals(object? obj)
    {
        return obj is ColorAttachment attachment && Equals(attachment);
    }

    public readonly bool Equals(ColorAttachment other)
    {
        return Format == other.Format && ClearColor == other.ClearColor;
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(Format, ClearColor);
    }
}