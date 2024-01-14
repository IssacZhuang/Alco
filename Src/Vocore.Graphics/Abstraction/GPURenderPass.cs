namespace Vocore.Graphics;

public abstract class GPURenderPass : BaseGPUObject
{
    public abstract IReadOnlyList<ColorAttachment> Colors { get; }
    public abstract DepthAttachment? Depth { get; }
    public abstract uint Width { get; }
    public abstract uint Height { get; }
    public abstract string Name { get; }
    public abstract void Resize(uint width, uint height);
}