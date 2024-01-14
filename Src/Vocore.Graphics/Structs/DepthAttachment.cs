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
}