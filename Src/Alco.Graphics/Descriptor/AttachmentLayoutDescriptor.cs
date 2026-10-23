namespace Alco.Graphics;

/// <summary>
/// Declares a frame buffer's color attachments and optional depth attachment,
/// including formats and clear values. The layout owns no textures; frame buffer
/// descriptors create or reference them.
/// </summary>
public struct AttachmentLayoutDescriptor
{
    /// <summary>
    /// Initializes the layout with its color attachments and optional depth attachment.
    /// </summary>
    public AttachmentLayoutDescriptor(ColorAttachment[] colors, DepthAttachment? depth, string name = "unnamed_attachment_layout")
    {
        Colors = colors;
        Depth = depth;
        Name = name;
    }
    /// <summary>
    /// The color attachments, in render-target order.
    /// </summary>
    public ColorAttachment[] Colors { get; init; }
    /// <summary>
    /// The depth attachment, or null when the layout has no depth attachment.
    /// </summary>
    public DepthAttachment? Depth { get; init; }
    /// <summary>
    /// Diagnostic name shown in errors and debuggers.
    /// </summary>
    public string Name { get; init; } = "unnamed_render_pass";

}