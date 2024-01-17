using System.Numerics;

namespace Vocore.Graphics;

public struct ColorAttachment
{
    public ColorAttachment(PixelFormat format)
    {
        Format = format;
    }
    
    public ColorAttachment(PixelFormat format, Vector4 clearColor)
    {
        Format = format;
        ClearColor = clearColor;
    }

    public PixelFormat Format { get; init; }
    public Vector4 ClearColor { get; init; } = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

    //override operator == and !=

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