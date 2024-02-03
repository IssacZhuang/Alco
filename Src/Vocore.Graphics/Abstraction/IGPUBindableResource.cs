using WebGPU;

namespace Vocore.Graphics;

public interface IGPUBindableResource
{
    BindableResourceType ResourceType { get; }
}