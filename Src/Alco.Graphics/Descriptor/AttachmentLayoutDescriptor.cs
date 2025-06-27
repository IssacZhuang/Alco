namespace Alco.Graphics;

public struct AttachmentLayoutDescriptor
{
    public AttachmentLayoutDescriptor(ColorAttachment[] colors, DepthAttachment? depth, string name = "unnamed_attachment_layout")
    {
        Colors = colors;
        Depth = depth;
        Name = name;
    }
    public ColorAttachment[] Colors { get; init; }
    public DepthAttachment? Depth { get; init; }
    public string Name { get; init; } = "unnamed_render_pass";

}