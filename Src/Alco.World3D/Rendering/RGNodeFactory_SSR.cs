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
/// Factory for the screen-space reflections render plugin node: holds the five
/// shaders (trace / resolve / composite / scene-copy blit / blue-noise bake,
/// resolved at load time) and the trace-resolution scale. The node's deferred
/// inputs — the G-buffer and scene color resources, the voxel GI node providing
/// the off-screen reflection fallback, the camera and the scene environment —
/// are factory context services the composing code registers (<see
/// cref="GBufferInput"/>, <see cref="SceneColorInput"/>,
/// <see cref="RGNode_VoxelGI"/>, <see cref="CameraPerspectiveBuffer"/>,
/// <see cref="PBRSceneEnvironment"/>, plus the post <see cref="RenderChain"/>);
/// graph insertion stays with the composing code through
/// <see cref="RGNode_SSR.Attach"/>.
/// </summary>
public class RGNodeFactory_SSR : RenderNodeFactory
{
    /// <summary>The reflection trace shader.</summary>
    public required Shader TraceShader { get; set; }
    /// <summary>The temporal/spatial resolve shader.</summary>
    public required Shader ResolveShader { get; set; }
    /// <summary>The composite shader.</summary>
    public required Shader CompositeShader { get; set; }
    /// <summary>The plain scene-copy shader.</summary>
    public required Shader SceneCopyShader { get; set; }
    /// <summary>The blue-noise tile bake shader.</summary>
    public required Shader BlueNoiseShader { get; set; }

    /// <summary>The trace resolution relative to the viewport (0.25 - 1.0).</summary>
    public float TraceResolutionScale { get; set; } = 0.5f;

    /// <inheritdoc />
    public override IRenderNode Create(RenderNodeFactoryContext context)
    {
        RenderNodeFactoryServices services = context.Services;
        return new RGNode_SSR(
            context.Rendering,
            context.Graph,
            services.Get<RenderChain>(),
            services.Get<GBufferInput>().Texture,
            services.Get<SceneColorInput>().Texture,
            services.Get<RGNode_VoxelGI>(),
            services.Get<CameraPerspectiveBuffer>(),
            services.Get<PBRSceneEnvironment>(),
            TraceShader,
            ResolveShader,
            CompositeShader,
            SceneCopyShader,
            BlueNoiseShader,
            context.Graph.Width,
            context.Graph.Height,
            traceResolutionScale: TraceResolutionScale);
    }
}
