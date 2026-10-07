namespace Alco.Graphics;

/// <summary>
/// Declares the attachment layout of a frame buffer: the color attachments, an
/// optional depth attachment and their formats/clear values. The layout itself
/// owns no textures; a <see cref="FrameBufferDescriptor"/> creates owned
/// textures from it while an <see cref="ExternalFrameBufferDescriptor"/>
/// references externally owned ones.
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