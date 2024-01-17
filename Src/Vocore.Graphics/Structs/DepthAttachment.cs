namespace Vocore.Graphics;

public struct DepthAttachment
{
    public DepthAttachment(PixelFormat format, float clearDepth = 1.0f, uint clearStencil = 0)
    {
        Format = format;
        ClearDepth = clearDepth;
        ClearStencil = clearStencil;
    }

    public PixelFormat Format { get; set; }
    public float ClearDepth { get; init; } = 1.0f;
    public uint ClearStencil { get; init; } = 0;

    //override operator == and !=
    public static bool operator ==(DepthAttachment left, DepthAttachment right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(DepthAttachment left, DepthAttachment right)
    {
        return !(left == right);
    }

    public readonly override bool Equals(object? obj)
    {
        return obj is DepthAttachment attachment && Equals(attachment);
    }

    public readonly bool Equals(DepthAttachment other)
    {
        return Format == other.Format &&
        ClearDepth == other.ClearDepth &&
        ClearStencil == other.ClearStencil;
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(Format, ClearDepth, ClearStencil);
    }
}