namespace Vocore.Graphics;

/// <surmmary>
/// The instance of the color attachments and depth attachment of a <see cref="GPURenderPass"/> </br>
/// Used as the render target of a shader
/// </surmmary>
public abstract class GPUFrameBuffer : BaseGPUObject
{
    public abstract GPURenderPass RenderPass { get; }
    public abstract IReadOnlyList<GPUTexture> Colors { get; }
    public abstract GPUTexture? Depth { get; }
    public abstract uint Width { get; }
    public abstract uint Height { get; }
}