namespace Vocore.Graphics;

public struct RenderPassDescriptor
{
    public ColorAttachment[] Colors { get; init; }
    public DepthAttachment Depth { get; init; }
}