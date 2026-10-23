using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the voxel global illumination render plugin node, holding its <see cref="VoxelGiDescriptor"/>; the material compiler comes from factory services and graph resources and lighting hookup stay with the composing code.
/// </summary>
public class RGNodeFactory_VoxelGI : RenderNodeFactory
{
    /// <summary>The node's construction data.</summary>
    public required VoxelGiDescriptor Descriptor { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        VoxelGiDescriptor descriptor = Descriptor;
        return new RGNode_VoxelGI(
            context.Rendering,
            context.Services.Get<MaterialCompiler>(),
            context.Graph.Width,
            context.Graph.Height,
            in descriptor);
    }
}
