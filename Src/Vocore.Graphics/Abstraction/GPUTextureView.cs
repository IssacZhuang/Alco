namespace Vocore.Graphics;

public abstract class GPUTextureView : BaseGPUObject
{
    public abstract GPUTexture Texture { get; }
    public abstract TextureViewDimension Dimension { get; }
}
