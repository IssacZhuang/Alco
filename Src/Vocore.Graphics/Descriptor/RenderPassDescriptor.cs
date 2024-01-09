namespace Vocore.Graphics;

public struct RenderPassDescriptor
{
    public RenderPassDescriptor(ColorAttachment[] colors, DepthAttachment? depth, uint width, uint height, string? name = null)
    {
        Colors = colors;
        Depth = depth;
        Width = width;
        Height = height;
        Name = name;
    }
    public ColorAttachment[] Colors { get; init; }
    public DepthAttachment? Depth { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public string? Name { get; init; }

}