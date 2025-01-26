using System.Runtime.CompilerServices;

namespace Alco.Graphics;

public abstract class GPUSampler : BaseGPUObject, IGPUBindableResource
{
    public BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.Sampler;
    }

    protected GPUSampler(in SamplerDescriptor descriptor): base(descriptor.Name)
    {
    }
}
