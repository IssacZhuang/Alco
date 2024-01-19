namespace Vocore.Graphics;

public struct RenderPassDescriptor
{
    public RenderPassDescriptor(ColorAttachment[] colors, DepthAttachment? depth, string name = "Unnamed Render Pass")
    {
        Colors = colors;
        Depth = depth;
        Name = name;
    }
    public ColorAttachment[] Colors { get; init; }
    public DepthAttachment? Depth { get; init; }
    public string Name { get; init; }

}