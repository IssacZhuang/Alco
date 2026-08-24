using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Factory for the voxel global illumination render plugin node: holds the
/// node's <see cref="VoxelGiDescriptor"/> (shader references resolve through
/// the shared shader system at load time). The material compiler composing the
/// per-surface voxelize feed comes from the factory context's services; graph
/// resources and the lighting hookup stay with the composing code through
/// <see cref="RGNode_VoxelGI.Attach"/>. The triangle voxelization shader is not
/// mapped here — it composes per material surface through the compiler.
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
