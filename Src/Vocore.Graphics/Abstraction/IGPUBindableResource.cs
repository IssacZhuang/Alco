namespace Vocore.Graphics;

public interface IGPUBindableResource
{
    /// <summary>
    /// The type of the bindable resource.
    /// </summary>
    BindableResourceType ResourceType { get; }
}