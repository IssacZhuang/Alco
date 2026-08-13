using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The handle of a composed PBR deferred pipeline, created by
/// <see cref="RenderPipelines.CreatePBRDeferred"/>: a bag of references to the
/// pipeline shell, the shared scene environment and every composed node/resource,
/// plus the ownership of the preset-only objects (layouts, materials, GPU timestamp
/// sampler). It has no frame logic of its own — the frame is driven by
/// <see cref="Pipeline"/>.<see cref="RenderPipeline.Render"/>, the scene state is
/// set on <see cref="Environment"/>, and the composition is modified through
/// <see cref="Graph"/> directly (insert custom nodes between any two stages, or
/// remove a stock stage and replace it with a custom implementation — nothing the
/// preset does requires engine internals).
/// </summary>
public sealed class PBRDeferredPreset : AutoDisposable
{
    private readonly RGNode_Callback _afterGBufferNode;
    private Action? _afterGBufferCallback;

    // Owned by the preset (not by the pipeline shell or the graph).
    private readonly GPUAttachmentLayout _gbufferLayout;
    private readonly GPUAttachmentLayout _shadowLayout;
    private readonly GPUAttachmentLayout _forwardLayout;
    private readonly GraphicsMaterial _lightingMaterial;
    private readonly GraphicsMaterial? _volumetricLightMaterial;
    private readonly GpuTimestampSampler? _gpuTimestamps;

    internal PBRDeferredPreset(
        RenderPipeline pipeline,
        PBRSceneEnvironment environment,
        RenderProfiler profiler,
        RenderGraphTexture gbufferResource,
        RenderGraphTexture shadowMapResource,
        RGNode_ShadowPass shadowPass,
        RGNode_GeometryPass gbufferPass,
        RGNode_Callback afterGBufferNode,
        RGNode_DeferredLighting lightingNode,
        RGNode_FullscreenOverlay? volumetricLightNode,
        GPUAttachmentLayout gbufferLayout,
        GPUAttachmentLayout shadowLayout,
        GPUAttachmentLayout forwardLayout,
        GraphicsMaterial lightingMaterial,
        GraphicsMaterial? volumetricLightMaterial,
        GpuTimestampSampler? gpuTimestamps)
    {
        Pipeline = pipeline;
        Environment = environment;
        Profiler = profiler;
        GBufferResource = gbufferResource;
        ShadowMapResource = shadowMapResource;
        ShadowPass = shadowPass;
        GBufferPass = gbufferPass;
        _afterGBufferNode = afterGBufferNode;
        Lighting = lightingNode;
        VolumetricLight = volumetricLightNode;
        _gbufferLayout = gbufferLayout;
        _shadowLayout = shadowLayout;
        _forwardLayout = forwardLayout;
        _lightingMaterial = lightingMaterial;
        _volumetricLightMaterial = volumetricLightMaterial;
        _gpuTimestamps = gpuTimestamps;
    }

    /// <summary>The pipeline shell driving the frame: <see cref="RenderPipeline.Render"/>,
    /// <see cref="RenderPipeline.Resize"/> and <see cref="RenderPipeline.Use"/> live here.</summary>
    public RenderPipeline Pipeline { get; }

    /// <summary>The shared scene state: sun/sky/shadow/GI/volumetric parameters, the
    /// camera, point lights and the shadow cascade fitting.</summary>
    public PBRSceneEnvironment Environment { get; }

    /// <summary>
    /// The render performance profiler (also set as <see cref="RenderGraph.Profiler"/>).
    /// Pipeline stages and registered plugins push per-frame timing data here.
    /// External UI reads snapshots via <see cref="RenderProfiler.GetSnapshot"/>.
    /// </summary>
    public RenderProfiler Profiler { get; }

    /// <summary>The render graph driving the frame (same as Pipeline.Graph).</summary>
    public RenderGraph Graph => Pipeline.Graph;

    /// <summary>The content chain threaded through the forward/post stages, rooted at
    /// <see cref="SceneColorResource"/> at the start of every frame.</summary>
    public RenderChain PostChain => Pipeline.Chain;

    /// <summary>The final blit node — the anchor post-process and forward content
    /// nodes are inserted before (see <see cref="RenderPipeline.Use"/>).</summary>
    public RGNode_Blit FinalBlit => Pipeline.FinalBlit;

    /// <summary>The G-buffer transient resource read by the lighting pass and
    /// effect plugins.</summary>
    public RenderGraphTexture GBufferResource { get; }

    /// <summary>The shadow map atlas transient resource.</summary>
    public RenderGraphTexture ShadowMapResource { get; }

    /// <summary>The scene color transient resource (HDR color + depth): the chain
    /// root and the lighting pass output.</summary>
    public RenderGraphTexture SceneColorResource => Pipeline.SceneColorResource;

    /// <summary>The shadow pass node. Register casters on
    /// <see cref="RGNode_ShadowPass.Content"/>.</summary>
    public RGNode_ShadowPass ShadowPass { get; }

    /// <summary>The G-buffer pass node. Register scene geometry on
    /// <see cref="RGNode_GeometryPass.Content"/>.</summary>
    public RGNode_GeometryPass GBufferPass { get; }

    /// <summary>The deferred lighting node. Wire effect plugin outputs through
    /// <see cref="RGNode_DeferredLighting.AoInput"/> /
    /// <see cref="RGNode_DeferredLighting.GiDiffuseInput"/> /
    /// <see cref="RGNode_DeferredLighting.GiSpecularInput"/> or
    /// <see cref="RGNode_DeferredLighting.ExtraReads"/>.</summary>
    public RGNode_DeferredLighting Lighting { get; }

    /// <summary>The volumetric light overlay node, or null when the preset was
    /// created without a volumetric light shader.</summary>
    public RGNode_FullscreenOverlay? VolumetricLight { get; }

    /// <summary>The attachment layout of the G-buffer pass, used to record render
    /// bundles (see <see cref="SubRenderContext.BeginPass"/>).</summary>
    public GPUAttachmentLayout GBufferLayout => _gbufferLayout;

    /// <summary>The attachment layout of the shadow pass, used to record render
    /// bundles (see <see cref="SubRenderContext.BeginPass"/>).</summary>
    public GPUAttachmentLayout ShadowLayout => _shadowLayout;

    /// <summary>The attachment layout of <see cref="ForwardRenderTexture"/>.</summary>
    public GPUAttachmentLayout ForwardLayout => _forwardLayout;

    /// <summary>The color-only sibling layout of the scene color layout, for
    /// post-process node outputs (<see cref="RGNode_FullscreenPass"/> and
    /// <see cref="RGNode_ChainTransform"/> derivatives).</summary>
    public GPUAttachmentLayout PostProcessLayout => Pipeline.PostProcessLayout;

    /// <summary>The width of one shadow cascade (atlas quadrant) in texels.</summary>
    public uint ShadowMapSize => Environment.ShadowMapSize;

    /// <summary>The G-buffer render texture (albedo+packed-roughness /
    /// detail+packed-geometric normal / metallic-roughness-ao /
    /// emissive+packed-geometric normal / depth). This is a stable facade over the
    /// graph's transient G-buffer: the object identity never changes.</summary>
    public RenderTexture GBuffer => GBufferResource.Texture;

    /// <summary>The depth-only shadow map render texture (a 2x2 cascade atlas). This
    /// is a stable facade over the graph's transient shadow map.</summary>
    public RenderTexture ShadowMap => ShadowMapResource.Texture;

    /// <summary>
    /// The pipeline-internal forward render texture (HDR color + depth) holding the
    /// resolved lighting and forward transparency — the frame output before
    /// post-processing. This is a stable facade: the backing graph textures change
    /// across frames (pooling / aliasing / resize), but the object identity never
    /// does and material bindings follow automatically through the version check.
    /// </summary>
    public RenderTexture ForwardRenderTexture => SceneColorResource.Texture;

    /// <summary>
    /// Called by the frame after the G-buffer pass, before the effect plugins.
    /// Use this to submit per-frame dynamic data (e.g. voxel GI instances).
    /// </summary>
    public event Action? AfterGBuffer
    {
        add
        {
            _afterGBufferCallback += value;
            _afterGBufferNode.Callback = InvokeAfterGBufferCallback;
        }
        remove
        {
            _afterGBufferCallback -= value;
            if (_afterGBufferCallback == null)
            {
                _afterGBufferNode.Callback = null;
            }
        }
    }

    private void InvokeAfterGBufferCallback(RenderGraphContext context)
    {
        _afterGBufferCallback?.Invoke();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The shell disposes the graph, which disposes every registered node (the
            // composed fixed nodes, chain nodes and attached plugins), the transient
            // facades and the texture pool.
            Pipeline.Dispose();
            Environment.Dispose();
            _lightingMaterial.Dispose();
            _volumetricLightMaterial?.Dispose();
            _gbufferLayout.Dispose();
            _shadowLayout.Dispose();
            _forwardLayout.Dispose();
            _gpuTimestamps?.Dispose();
        }
    }
}
