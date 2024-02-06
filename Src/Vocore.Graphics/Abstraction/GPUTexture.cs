namespace Vocore.Graphics;

/// <summary>
/// The texture in the VRAM
/// </summary>
public abstract class GPUTexture : BaseGPUObject
{
    public abstract uint Width { get; }
    public abstract uint Height { get; }
    public abstract uint Depth { get; }
    public abstract uint MipLevelCount { get; }
}