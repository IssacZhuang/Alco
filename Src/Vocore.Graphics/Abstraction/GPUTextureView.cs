using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

public abstract class GPUTextureView : BaseGPUObject, IGPUBindableResource
{
    public abstract GPUTexture Texture { get; }
    public abstract TextureViewDimension Dimension { get; }
    public BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.TextureView;
    }
}
