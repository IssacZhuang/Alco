namespace Vocore.Graphics;

public abstract class GPURenderPass : BaseGPUObject
{
    public abstract IReadOnlyList<GPUTexture> Colors { get; }
    public abstract GPUTexture? Depth { get; }
    public abstract uint Width { get; }
    public abstract uint Height { get; }
    public abstract string Name { get; }
    public abstract void Resize(uint width, uint height);
}