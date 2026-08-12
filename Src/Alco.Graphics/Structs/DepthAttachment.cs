namespace Alco.Graphics;

public struct DepthAttachment
{
    public DepthAttachment(PixelFormat format, float clearDepth = 1.0f, uint clearStencil = 0)
    {
        Format = format;
        ClearDepth = clearDepth;
        ClearStencil = clearStencil;
        ReadOnly = false;
    }

    public PixelFormat Format { get; set; }
    public float ClearDepth { get; init; } = 1.0f;
    public uint ClearStencil { get; init; } = 0;

    /// <summary>
    /// Whether render passes on frame buffers of this layout attach the depth-stencil
    /// attachment as read-only (webgpu <c>depthReadOnly</c>/<c>stencilReadOnly</c>).
    /// Required when the same depth texture is simultaneously sampled by the pass
    /// (e.g. a scene color target sharing the G-buffer's depth while the lighting
    /// pass samples that depth). Pipelines used in such passes must not enable
    /// depth/stencil writes, and the pass cannot clear depth/stencil.
    /// </summary>
    public bool ReadOnly { get; init; }

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
        ClearStencil == other.ClearStencil &&
        ReadOnly == other.ReadOnly;
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(Format, ClearDepth, ClearStencil, ReadOnly);
    }
}