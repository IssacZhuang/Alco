using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The G-buffer graph resource as a factory service role. Registered by the
/// composing code (which created the resource) so SSR-family factories can pull
/// the deferred inputs type-safely — <see cref="RenderGraphTexture"/> alone
/// cannot distinguish the G-buffer from the scene color target.
/// </summary>
public readonly record struct GBufferInput(RenderGraphTexture Texture);

/// <summary>
/// The HDR scene color graph resource as a factory service role — the
/// <see cref="GBufferInput"/> counterpart for SSR-family factories.
/// </summary>
public readonly record struct SceneColorInput(RenderGraphTexture Texture);

/// <summary>
/// Factory for the screen-space reflections render plugin node: holds the
/// node's <see cref="RGNode_SSR.Descriptor"/> (shader references resolve
/// through the shared shader system at load time). The node's deferred inputs —
/// the G-buffer and scene color resources, the voxel GI node providing the
/// off-screen reflection fallback, the camera and the scene environment — are
/// factory context services the composing code registers (<see
/// cref="GBufferInput"/>, <see cref="SceneColorInput"/>,
/// <see cref="RGNode_VoxelGI"/>, <see cref="CameraPerspectiveBuffer"/>,
/// <see cref="PBRSceneEnvironment"/>, plus the post <see cref="RenderChain"/>);
/// graph insertion stays with the composing code through
/// <see cref="RGNode_SSR.Attach"/>.
/// </summary>
public class RGNodeFactory_SSR : RenderNodeFactory
{
    /// <summary>The node's construction data.</summary>
    public required RGNode_SSR.Descriptor Descriptor { get; set; }

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        RenderNodeFactoryServices services = context.Services;
        RGNode_SSR.Descriptor descriptor = Descriptor;
        return new RGNode_SSR(
            context.Rendering,
            context.Graph,
            services.Get<RenderChain>(),
            services.Get<GBufferInput>().Texture,
            services.Get<SceneColorInput>().Texture,
            services.Get<RGNode_VoxelGI>(),
            services.Get<CameraPerspectiveBuffer>(),
            services.Get<PBRSceneEnvironment>(),
            context.Graph.Width,
            context.Graph.Height,
            in descriptor);
    }
}
