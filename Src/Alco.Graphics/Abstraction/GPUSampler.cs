using System.Runtime.CompilerServices;

namespace Alco.Graphics;

/// <summary>
/// A texture sampling state object (filtering and addressing) bound as a bindable resource.
/// </summary>
public abstract class GPUSampler : BaseGPUObject, IGPUBindableResource
{
    /// <summary>
    /// Gets the bindable-resource discriminator; always <see cref="BindableResourceType.Sampler"/>.
    /// </summary>
    public BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.Sampler;
    }

    protected GPUSampler(in SamplerDescriptor descriptor): base(descriptor.Name)
    {
    }
}
