using System.Runtime.CompilerServices;

namespace Alco.Graphics;

/// <summary>
/// A view of a GPU texture binding a subset of its mips, layers, or aspects.
/// </summary>
public abstract class GPUTextureView : BaseGPUObject, IGPUBindableResource
{
    /// <summary>
    /// The texture that this view is associated with.
    /// </summary>
    public abstract GPUTexture Texture { get; }


    /// <summary>
    /// Gets the bindable-resource discriminator; always <see cref="BindableResourceType.TextureView"/>.
    /// </summary>
    public BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.TextureView;
    }

    /// <summary>Initializes the view from a descriptor.</summary>
    /// <param name="descriptor">The descriptor for the GPU texture view.</param>
    protected GPUTextureView(in TextureViewDescriptor descriptor): base(descriptor.Name)
    {
    }

    /// <summary>Initializes the view with only a diagnostic name; used by views created later around an existing native view.</summary>
    /// <param name="name">The diagnostic name of the view.</param>
    public GPUTextureView(string name): base(name)
    {
    }
}
