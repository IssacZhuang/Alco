namespace Alco.Graphics;

/// <summary>
/// The load and store operations of a render pass attachment.
/// </summary>
public readonly struct AttachmentOps
{
    /// <summary>
    /// The default attachment operations (<see cref="AttachmentLoadOp.Load"/> and <see cref="AttachmentStoreOp.Store"/>).
    /// </summary>
    public static readonly AttachmentOps Default = new();

    /// <summary>
    /// The load operation of the attachment at the beginning of the render pass.
    /// <br/> A clear value specified through <see cref="GPUCommandBuffer.BeginRender"/> takes precedence over this operation.
    /// </summary>
    public AttachmentLoadOp LoadOp { get; init; }

    /// <summary>
    /// The store operation of the attachment at the end of the render pass.
    /// </summary>
    public AttachmentStoreOp StoreOp { get; init; }
}
