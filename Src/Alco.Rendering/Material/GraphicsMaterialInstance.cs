using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The instance of the <see cref="GraphicsMaterial"/> which used to override the parameters of the parent material.
/// Slot values not set on the instance are resolved from the parent chain when the resource
/// groups are assembled, so the groups are complete on their own.
/// </summary>
public sealed class GraphicsMaterialInstance : GraphicsMaterial
{
    private readonly GraphicsMaterial _parent;

    internal GraphicsMaterialInstance(RenderingSystem system, GraphicsMaterial parent)
        : base(system, parent.Shader, $"{parent.Name}_instance", parent.Specializations.ToArray())
    {
        _parent = parent;
        _parameters.Fallback = parent.Parameters;
        _pipelineContext = new GraphicsPipelineContext(
            parent.ReflectionInfo,
            parent.DepthStencilState,
            parent.BlendState,
            parent.RasterizerState,
            parent.PrimitiveTopology
            )
        {
            Specializations = parent.Specializations.ToArray(),
        };
    }

    /// <summary>
    /// Does nothing: slot values are inherited through the parameter set fallback chain.
    /// </summary>
    /// <remarks>
    /// Binding the white-texture defaults here would write them into own slots,
    /// which resolve before the fallback and so shadow the parent-bound textures.
    /// </remarks>
    protected override void UpdateSlotResources(ShaderReflection reflectionInfo) { }
}
