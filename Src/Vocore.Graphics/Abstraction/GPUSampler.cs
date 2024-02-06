using System.Runtime.CompilerServices;

namespace Vocore.Graphics;

public abstract class GPUSampler : BaseGPUObject, IGPUBindableResource
{
    public BindableResourceType ResourceType
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BindableResourceType.Sampler;
    }
}
