namespace Vocore.Graphics;

public struct ColorAttachment
{
    public ColorAttachment(PixelFormat format)
    {
        Format = format;
    }
    public PixelFormat Format { get; init; }
}