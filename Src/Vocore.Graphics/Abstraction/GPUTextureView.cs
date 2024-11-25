using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

public abstract class GPUTextureView : BaseGPUObject, IGPUBindableResource
{
    /// <summary>
    /// The texture that this view is associated with.  
    /// </summary>
    public abstract GPUTexture Texture { get; }


    public BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.TextureView;
    }

    protected GPUTextureView(in TextureViewDescriptor descriptor): base(descriptor.Name)
    {
    }

    public GPUTextureView(string name): base(name)
    {
    }
}
