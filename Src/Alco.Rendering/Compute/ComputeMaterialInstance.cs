namespace Alco.Rendering;

/// <summary>
/// The instance of the <see cref="ComputeMaterial"/> which used to override the parameters of the parent compute dispatcher.
/// This allows for creating variations of a compute dispatcher while sharing resources with the parent.
/// Slot values not set on the instance are resolved from the parent chain when the resource
/// groups are assembled, so the groups are complete on their own.
/// </summary>
public sealed class ComputeMaterialInstance : ComputeMaterial
{
    private readonly ComputeMaterial _parent;

    internal ComputeMaterialInstance(RenderingSystem system, ComputeMaterial parent) : base(system, parent.Shader)
    {
        _parent = parent;
        _parameterSet.Fallback = parent.ParameterSet;
        _pipelineContext = new ComputePipelineContext(
            parent.ReflectionInfo,
            parent.Defines
            );
    }
}
