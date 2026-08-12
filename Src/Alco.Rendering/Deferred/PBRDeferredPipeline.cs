using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A deferred PBR rendering pipeline built on the engine's WebGPU resources, driven
/// internally by a <see cref="RenderGraph"/>.
/// <br/>Owns the graph's transient targets — a G-buffer (albedo / normal /
/// metallic-roughness-ao / emissive + depth), a depth-only shadow map holding
/// <see cref="ShadowCascadeCount"/> cascades in a 2x2 atlas and the scene color
/// target (HDR color + depth) — plus the pass
/// render contexts and the pass-private deferred lighting material. G-buffer scene
/// draws and shadow scene draws are handled by render nodes
/// (<see cref="GBufferRenderer"/> / <see cref="ShadowRenderer"/>) registered via
/// <see cref="Use"/>; the pipeline does not know their types — it invokes
/// <see cref="IGBufferRenderNode"/> / <see cref="IShadowRenderNode"/> inside the
/// owning graph nodes.
/// <br/>The frame is driven by <see cref="Render"/>, which executes the graph:
/// shadow cascades → G-buffer → AfterGBuffer plugins → deferred lighting into the
/// scene color target → volumetric light → forward content nodes (transparency,
/// hardware depth-tested against the G-buffer depth) →
/// content processors → final blit into the destination. Transient targets are
/// pooled and aliased by the graph, unused work is culled automatically (disabled
/// shadows, disabled volumetric light, processors on headless frames) and the
/// whole frame is submitted as a single command batch.
/// <br/>Scene properties (sun direction/color, sky params, GI strength, debug flags) are
/// set directly on the pipeline as properties. Camera, shadow cascades, viewport and
/// point-light count are managed internally — the caller only calls
/// <see cref="SetCamera"/>, <see cref="ComputeShadowCascades"/> and
/// <see cref="UpdatePointLights"/>.
/// <br/>The per-cascade shadow view-projections live in a uniform buffer with reference
/// semantics, so render bundles recorded against <see cref="ShadowLayout"/> stay valid
/// while the camera-fitted cascades move.
/// <br/>Cascade splits are computed by <see cref="ComputeShadowCascades"/> (PSSM,
/// camera-fitted, texel-snapped).
/// <br/>Effects (HBAO, VoxelGI, SSR) are registered via <see cref="Use"/> as direct
/// render graph nodes between the G-buffer pass and the lighting pass; their output
/// textures are bound to the lighting material automatically.
/// </summary>
public sealed unsafe class PBRDeferredPipeline : AutoDisposable
{
    /// <summary>
    /// Per-frame shadow pass data uploaded to the <c>_data</c> uniform buffer of the
    /// shadow depth shaders: the quadrant-folded light view-projection matrix of each
    /// cascade. Layout must match the <c>_data</c> cbuffer in ShadowDepth.hlsl exactly.
    /// </summary>
    internal struct ShadowCascadeData
    {
        /// <summary>Light view-projection matrix of shadow cascade 0 (nearest).</summary>
        public Matrix4x4 CascadeViewProjection0;
        /// <summary>Light view-projection matrix of shadow cascade 1.</summary>
        public Matrix4x4 CascadeViewProjection1;
        /// <summary>Light view-projection matrix of shadow cascade 2.</summary>
        public Matrix4x4 CascadeViewProjection2;
        /// <summary>Light view-projection matrix of shadow cascade 3 (farthest).</summary>
        public Matrix4x4 CascadeViewProjection3;
    }

    /// <summary>
    /// A point light: position with range in world space and linear color (rgb)
    /// plus intensity (w). Uploaded as elements of a StructuredBuffer to the GPU.
    /// </summary>
    public struct PointLight
    {
        /// <summary>World-space position (xyz) and effective range / cutoff radius (w).</summary>
        public Vector4 Position;
        /// <summary>Linear color (rgb) and intensity (w). Zero intensity disables the light.</summary>
        public Vector4 ColorAndIntensity;

        /// <summary>
        /// Create a point light with a custom range.
        /// </summary>
        /// <param name="position">World-space position.</param>
        /// <param name="color">Linear color.</param>
        /// <param name="intensity">Light intensity; zero disables the light.</param>
        /// <param name="range">Cutoff radius beyond which the light contributes nothing.</param>
        public PointLight(in Vector3 position, in Vector3 color, float intensity, float range)
        {
            Position = new Vector4(position, range);
            ColorAndIntensity = new Vector4(color, intensity);
        }
    }

    /// <summary>The number of shadow cascades (atlas quadrants) the pipeline supports.</summary>
    public const int ShadowCascadeCount = 4;

    /// <summary>The maximum number of point lights the StructuredBuffer can hold.</summary>
    public const int MaxPointLights = 256;

    /// <summary>
    /// Per-frame data uploaded to the lighting pass. Layout must match the
    /// <c>_data</c> cbuffer in DeferredLighting.hlsl exactly. Assembled internally
    /// from caller-set properties (<see cref="SunDirection"/>, <see cref="SkyParams"/>,
    /// etc.) and pipeline-owned data (camera, cascades, viewport).
    /// </summary>
    internal struct DeferredLightingData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>Sun light view-projection matrix of shadow cascade 0 (nearest).</summary>
        public Matrix4x4 SunViewProjection0;
        /// <summary>Sun light view-projection matrix of shadow cascade 1.</summary>
        public Matrix4x4 SunViewProjection1;
        /// <summary>Sun light view-projection matrix of shadow cascade 2.</summary>
        public Matrix4x4 SunViewProjection2;
        /// <summary>Sun light view-projection matrix of shadow cascade 3 (farthest).</summary>
        public Matrix4x4 SunViewProjection3;
        /// <summary>Camera position in world space (w unused).</summary>
        public Vector4 CameraPosition;
        /// <summary>Normalized direction the sun light travels (w unused).</summary>
        public Vector4 SunDirection;
        /// <summary>Sun linear color (rgb) and intensity (w).</summary>
        public Vector4 SunColorAndIntensity;
        /// <summary>Atmosphere parameters: x=rayleighScale, y=mieScale, z=miePhaseG, w=exposure (see Atmosphere.hlsli).</summary>
        public Vector4 SkyParams;
        /// <summary>Atmosphere parameters: x=starIntensity, y=nightFloor, z=sunRadianceScale, w=ambientFloor (minimum hemisphere ambient multiplier).</summary>
        public Vector4 SkyParams2;
        /// <summary>Azimuthally filtered physical-sky radiance at the horizon.</summary>
        public Vector4 SkyHorizonColor;
        /// <summary>Filtered physical-sky radiance at the zenith.</summary>
        public Vector4 SkyZenithColor;
        /// <summary>x=shadowEnabled y=numPointLights z=shadowMapSize w=sunDiscEnabled.</summary>
        public Vector4 Params;
        /// <summary>View-distance end boundary of each cascade; beyond w there is no shadow.</summary>
        public Vector4 CascadeSplits;
        /// <summary>World units per shadow texel of each cascade (for the normal-offset bias).</summary>
        public Vector4 CascadeTexelSizes;
        /// <summary>x=cascadeDebugTint, y=shadowFactorView, z=unused, w=aoDebugView.</summary>
        public Vector4 Params2;
        /// <summary>xy=render target size in pixels (filled by the pipeline).</summary>
        public Vector4 ViewportSize;
        /// <summary>x=giEnabled, y=giDiffuseStrength, z=giSpecularStrength, w=giDebugView (0=off 1=diffuse 2=specular 3=visibility).</summary>
        public Vector4 Params3;
        /// <summary>x=sunDiscSize (cosine angular threshold, higher = smaller disc), y=sunDiscBrightness (HDR visual brightness independent of lighting intensity), z=1/GI trace width, w=1/GI trace height (filled by the pipeline, 0 when GI is off).</summary>
        public Vector4 Params4;
        /// <summary>Volumetric light params: x=enabled(&gt;0), y=fogDensity, z=heightScaleHeight (height-falloff model, ignored for constant), w=phaseG (Henyey-Greenstein anisotropy).</summary>
        public Vector4 VLParams;

    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly Mesh _fullScreenMesh;

    // Pass nodes (GBufferRenderer, ShadowRenderer, ...) invoked inside the pipeline's
    // own passes, dispatched by the node interfaces they implement.
    private readonly List<IRenderNode> _passNodes = new();

    // The render graph driving the frame, plus its transient targets. The scene
    // color has its own depth attachment, so
    // the forward pass depth-tests against its own depth.
    private readonly RenderGraph _graph;
    private readonly RenderGraphTexture _gbufferResource;
    private readonly RenderGraphTexture _shadowMapResource;
    private readonly RenderGraphTexture _sceneColorResource;

    // Post-chain threading state: reset to the scene color resource at the start of
    // every frame, advanced by each enabled post-process node during graph Setup.
    private readonly PostChainState _postChain = new();

    // The fixed graph nodes, in execution order. The blit node is re-registered
    // last whenever a chain node is added via Use.
    private readonly ShadowPassNode _shadowNode;
    private readonly GBufferPassNode _gbufferNode;
    private readonly CallbackNode _callbackNode;
    private readonly LightingNode _lightingNode;
    private readonly VolumetricLightNode _volumetricLightNode;
    private readonly BlitNode _blitNode;

    // The graph adapters of the chain-registered nodes (IForwardRenderNode /
    // IContentProcessorNode), in registration order.
    private readonly List<IChainAdapterNode> _chainAdapters = new();

    // Output transients created by the graph (HBAO / VoxelGI full-resolution
    // results), or null when the corresponding renderer is not registered.
    private RenderGraphTexture? _aoImport;
    private RenderGraphTexture? _giDiffuseImport;
    private RenderGraphTexture? _giSpecularImport;

    // Whether the current frame has no final destination; set at the start of
    // Render and read by the nodes' Setup.
    private bool _frameDestinationNull;

    private readonly GPUAttachmentLayout _gbufferLayout;
    private readonly GPUAttachmentLayout _shadowLayout;
    private readonly GPUAttachmentLayout _forwardLayout;
    private readonly GPUAttachmentLayout _postProcessLayout;

    private readonly GraphicsMaterial _lightingMaterial;
    private GraphicsMaterial? _volumetricLightMaterial;
    private readonly RenderContext _volumetricLightContext;
    private CameraPerspectiveBuffer? _camera;

    private readonly GraphicsValueBuffer<DeferredLightingData> _lightingDataBuffer;
    private readonly GraphicsValueBuffer<ShadowCascadeData> _shadowDataBuffer;
    private readonly GraphicsArrayBuffer<PointLight> _pointLightBuffer;

    /// <summary>
    /// Called by <see cref="Render"/> after the G-buffer pass, before AfterGBuffer
    /// plugins. Use this to submit per-frame dynamic data (e.g. voxel GI instances).
    /// </summary>
    public event Action? AfterGBufferCallback;

    /// <summary>
    /// The pipeline-internal forward render texture (HDR color + depth) holding the
    /// resolved lighting and forward transparency — the output of <see cref="Render"/>.
    /// This is a stable facade: the backing graph textures change across frames
    /// (pooling / aliasing / resize), but the object identity never does and material
    /// bindings follow automatically through the version check.
    /// </summary>
    public RenderTexture ForwardRenderTexture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _sceneColorResource.Texture;
    }

    /// <summary>
    /// The attachment layout of <see cref="ForwardRenderTexture"/>.
    /// </summary>
    public GPUAttachmentLayout ForwardLayout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _forwardLayout;
    }

    // Cascade state computed by ComputeShadowCascades and consumed by both the
    // shadow pass and the lighting pass — no longer exposed to the caller.
    private readonly Matrix4x4[] _cascadeViewProjections = new Matrix4x4[ShadowCascadeCount];
    private readonly float[] _cascadeSplits = new float[ShadowCascadeCount];
    private readonly float[] _cascadeTexelSizes = new float[ShadowCascadeCount];

    // Assembled internally from properties + camera + cascade state each frame.
    private DeferredLightingData _lightingData;
    private int _pointLightCount;
    private bool _giActive;

    // Render performance profiler — exposes per-stage timing (shadow / G-buffer /
    // lighting / plugins) to external UI. Counter handles are registered once in
    // the constructor; per-frame pushes use the int handle, never allocating.
    private readonly RenderProfiler _profiler = new();
    private RenderProfileCounterId _shadowCounter;
    private RenderProfileCounterId _gbufferCounter;
    private RenderProfileCounterId _lightingCounter;
    private RenderProfileCounterId _volumetricLightCounter;

    // GPU timestamp ring buffer for per-stage GPU timing. Each stage (G-buffer /
    // lighting) owns 2 query slots (begin + end) in a shared query set. The ring
    // buffer provides per-frame readback with a 2-frame latency, no stalls.
    private const int PipelineTimestampCount = 8; // 4 stages × 2 (begin/end)
    internal const int ShadowQueryBase = 0;
    internal const int GBufferQueryBase = 2;
    internal const int LightingQueryBase = 4;
    internal const int VolumetricLightQueryBase = 6;
    private GpuTimestampSampler? _gpuTimestamps;

    private readonly RenderContext _shadowContext;
    private readonly RenderContext _gbufferContext;
    private readonly RenderContext _lightingContext;

    /// <summary>
    /// The G-buffer render texture (albedo+packed-roughness /
    /// detail+packed-geometric normal / metallic-roughness-ao /
    /// emissive+packed-geometric normal / depth). This is a stable facade over the
    /// graph's transient G-buffer: the object identity never changes.
    /// </summary>
    public RenderTexture GBuffer => _gbufferResource.Texture;

    /// <summary>
    /// The depth-only shadow map render texture (a 2x2 cascade atlas). This is a
    /// stable facade over the graph's transient shadow map.
    /// </summary>
    public RenderTexture ShadowMap => _shadowMapResource.Texture;

    /// <summary>
    /// The render performance profiler. Pipeline stages and registered plugins push
    /// per-frame timing data here. External UI reads snapshots via
    /// <see cref="RenderProfiler.GetSnapshot"/>.
    /// </summary>
    public RenderProfiler Profiler => _profiler;

    /// <summary>
    /// The width of one shadow cascade (atlas quadrant) in texels.
    /// </summary>
    public uint ShadowMapSize { get; }

    // ── Scene properties (caller-set each frame) ──

    /// <summary>Normalized direction the sun light travels.</summary>
    public Vector3 SunDirection { get; set; }

    /// <summary>Linear sun color (rgb).</summary>
    public Vector3 SunColor { get; set; } = Vector3.One;

    /// <summary>Sun light intensity multiplier.</summary>
    public float SunIntensity { get; set; } = 1.0f;

    /// <summary>Whether cascaded shadow mapping is enabled.</summary>
    public bool ShadowEnabled { get; set; } = true;

    /// <summary>Distance beyond which shadows are not rendered.</summary>
    public float ShadowDistance { get; set; }

    /// <summary>How far the light-space depth range extends toward the sun for off-screen casters, in world units.</summary>
    public float ShadowCasterExtension { get; set; } = 20.0f;

    /// <summary>PSSM split blend: 1 = fully logarithmic, 0 = fully uniform.</summary>
    public float ShadowSplitLambda { get; set; } = 0.6f;

    /// <summary>Whether the physical-sky sun disc is visible.</summary>
    public bool SunDiscEnabled { get; set; } = true;

    /// <summary>Sun disc cosine angular threshold (higher = smaller disc).</summary>
    public float SunDiscSize { get; set; } = 0.9995f;

    /// <summary>Sun disc HDR visual brightness (independent of lighting intensity).</summary>
    public float SunDiscBrightness { get; set; } = 18.0f;

    /// <summary>Atmosphere params: x=rayleighScale, y=mieScale, z=miePhaseG, w=exposure.</summary>
    public Vector4 SkyParams { get; set; } = new(1.0f, 0.3f, 0.9f, 1.0f);

    /// <summary>Atmosphere params: x=starIntensity, y=nightFloor, z=sunRadianceScale, w=ambientFloor.</summary>
    public Vector4 SkyParams2 { get; set; } = new(1.0f, 0.05f, 20.0f, 0.25f);

    /// <summary>Filtered physical-sky radiance at the horizon.</summary>
    public Vector3 SkyHorizonColor { get; set; }

    /// <summary>Filtered physical-sky radiance at the zenith.</summary>
    public Vector3 SkyZenithColor { get; set; }

    /// <summary>Tint shadow cascade quadrants for debugging.</summary>
    public bool CascadeDebug { get; set; }

    /// <summary>Visualize shadow factor instead of applying shadows.</summary>
    public bool ShadowDebug { get; set; }

    /// <summary>Visualize ambient occlusion only.</summary>
    public bool AoDebugView { get; set; }

    /// <summary>Whether GI contributes to the lighting pass.</summary>
    public bool GiEnabled { get; set; } = true;

    /// <summary>Diffuse GI strength multiplier.</summary>
    public float GiDiffuseStrength { get; set; } = 1.0f;

    /// <summary>Specular GI strength multiplier.</summary>
    public float GiSpecularStrength { get; set; } = 1f;

    /// <summary>GI debug view mode (0=off 1=diffuse 2=specular 3=visibility).</summary>
    public int GiDebugView { get; set; }

    /// <summary>Whether volumetric light (god rays) contributes to the frame.</summary>
    public bool VolumetricLightEnabled { get; set; } = false;

    /// <summary>Volumetric light intensity multiplier (overall brightness of light shafts).</summary>
    public float VolumetricLightIntensity { get; set; } = 0.5f;

    /// <summary>Volumetric fog density (extinction coefficient; higher = thicker fog).</summary>
    public float VolumetricLightDensity { get; set; } = 0.002f;

    /// <summary>
    /// Scale height for the height-falloff density model. Fog density decays
    /// exponentially above ground level with this height constant. Only used
    /// when the shader is compiled with VL_DENSITY_HEIGHT_FALLOFF.
    /// </summary>
    public float VolumetricLightHeightScale { get; set; } = 5.0f;

    /// <summary>Henyey-Greenstein phase anisotropy g (0=isotropic, >0=forward scattering).</summary>
    public float VolumetricLightPhaseG { get; set; } = 0.9f;

    /// <summary>
    /// The attachment layout of the G-buffer pass, used to record render bundles
    /// (see <see cref="SubRenderContext.Begin(GPUAttachmentLayout)"/>).
    /// </summary>
    public GPUAttachmentLayout GBufferLayout => _gbufferLayout;

    /// <summary>
    /// The attachment layout of the shadow pass, used to record render bundles
    /// (see <see cref="SubRenderContext.Begin(GPUAttachmentLayout)"/>).
    /// </summary>
    public GPUAttachmentLayout ShadowLayout => _shadowLayout;

    /// <summary>
    /// The cascade VP data buffer (per-cascade light view-projection matrices).
    /// Passed to <see cref="ShadowRenderer"/> so its materials can bind it.
    /// </summary>
    public GraphicsBuffer ShadowDataBuffer => _shadowDataBuffer;

    /// <summary>
    /// The deferred lighting data buffer (per-frame sun, sky, cascade and camera
    /// constants). Shared with the forward renderer so it can evaluate the same PBR.
    /// </summary>
    public GraphicsBuffer LightingDataBuffer => _lightingDataBuffer;

    /// <summary>
    /// The live G-buffer render context for immediate (per-frame dynamic) draws.
    /// Only valid while the pipeline's G-buffer pass is recording (inside
    /// <see cref="IGBufferRenderNode.OnRenderGBuffer"/>).
    /// </summary>
    public IRenderContext GBufferContext => _gbufferContext;

    /// <summary>
    /// The live shadow render context for immediate (per-frame dynamic) draws.
    /// Only valid while the pipeline's shadow pass is recording (inside
    /// <see cref="IShadowRenderNode.OnRenderShadow"/>).
    /// </summary>
    public IRenderContext ShadowContext => _shadowContext;

    /// <summary>
    /// Create the deferred PBR pipeline.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="lightingShaderText">The source text of the deferred lighting shader (DeferredLighting.hlsl).</param>
    /// <param name="lightingShaderName">The name of the deferred lighting shader.</param>
    /// <param name="blitShader">The shader the final blit uses for plain copies.</param>
    /// <param name="shadowMapSize">The per-cascade shadow map resolution in texels; the shadow map is a 2x2 atlas of <see cref="ShadowCascadeCount"/> cascades, so the actual texture is twice this size along each axis.</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <param name="volumetricLightShader">Optional volumetric light (god rays) shader.
    /// When non-null the pipeline creates an additive blend pass that runs after
    /// deferred lighting. Pass null to skip volumetric light entirely.</param>
    public PBRDeferredPipeline(
        RenderingSystem rendering,
        string lightingShaderText,
        string lightingShaderName,
        Shader blitShader,
        uint shadowMapSize = 2048,
        uint width = 1280,
        uint height = 720,
        Shader? volumetricLightShader = null)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _fullScreenMesh = rendering.MeshFullScreen;
        ShadowMapSize = shadowMapSize;

        // The lighting shader declares its depth textures with the DEFINE_TEX2D_DEPTH*
        // macros, so the reflection already carries the Depth sample type and the
        // comparison sampler; the pipeline layout is built from the reflection.
        Shader lightingShader = rendering.CreateShader(lightingShaderText, lightingShaderName);

        _gbufferLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [
                // RGBA8Unorm + manual sRGB encode/decode: wgpu forbids STORAGE_BINDING
                // usage on sRGB textures, and engine framebuffer textures always carry it.
                new ColorAttachment(PixelFormat.RGBA8Unorm),
                new ColorAttachment(PixelFormat.RGBA16Float),
                new ColorAttachment(PixelFormat.RGBA8Unorm),
                // Linear emissive, HDR-capable.
                new ColorAttachment(PixelFormat.RGBA16Float),
            ],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_gbuffer_pass"));

        _shadowLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_shadow_pass"));

        // Scene color: HDR color + Depth32Float shared from the G-buffer (the depth
        // formats must match for the graph's depth sharing).
        _forwardLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(rendering.PreferredHDRFormat)],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_forward_pass"));

        // Color-only sibling of the scene color layout for post-process outputs.
        _postProcessLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(rendering.PreferredHDRFormat)],
            null,
            "pbr_post_process"));

        // The render graph and its transient targets. The 2x2 cascade atlas uses an
        // absolute size; the G-buffer and scene color follow the graph viewport.
        _graph = new RenderGraph(rendering, width, height, "pbr_deferred");
        _graph.Profiler = _profiler;
        _shadowMapResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
            _shadowLayout, shadowMapSize * 2, shadowMapSize * 2, name: "pbr_shadow_map"));
        _gbufferResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
            _gbufferLayout, name: "pbr_gbuffer"));
        _sceneColorResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
            _forwardLayout, name: "pbr_scene_color"));

        _shadowDataBuffer = rendering.CreateGraphicsValueBuffer<ShadowCascadeData>("pbr_shadow_data");

        // IMPORTANT: DepthStencilState.None means depthCompare=Never — with a depth
        // attachment present (the engine's HDR main target), every fragment would be
        // rejected. Default (Always) disables the depth test without rejecting pixels.
        _lightingMaterial = rendering.CreateMaterial(lightingShader);
        _lightingMaterial.DepthStencilState = DepthStencilState.Default;
        _lightingMaterial.RasterizerState = RasterizerState.CullNone;

        _lightingDataBuffer = rendering.CreateGraphicsValueBuffer<DeferredLightingData>("pbr_lighting_data");
        _lightingMaterial.SetBuffer(ShaderResourceId.Data, _lightingDataBuffer);

        // Point lights are uploaded as a StructuredBuffer (not cbuffer) so the
        // count is bounded only by GPU memory, not by cbuffer size limits.
        _pointLightBuffer = rendering.CreateGraphicsArrayBuffer<PointLight>(MaxPointLights, "pbr_point_lights");
        _lightingMaterial.SetBuffer(ShaderResourceId.PointLights, _pointLightBuffer);

        RebindLightingTargets();

        _shadowContext = rendering.CreateRenderContext("pbr_shadow_pass");
        _gbufferContext = rendering.CreateRenderContext("pbr_gbuffer_pass");
        _lightingContext = rendering.CreateRenderContext("pbr_lighting_pass");
        _volumetricLightContext = rendering.CreateRenderContext("pbr_volumetric_light_pass");

        // The fixed nodes, in execution order. Use() inserts chain nodes before the
        // blit node, which always stays last.
        _shadowNode = new ShadowPassNode(this);
        _gbufferNode = new GBufferPassNode(this);
        _callbackNode = new CallbackNode(this);
        _lightingNode = new LightingNode(this);
        _volumetricLightNode = new VolumetricLightNode(this);
        _blitNode = new BlitNode(this, blitShader);
        _graph.Use(_shadowNode);
        _graph.Use(_gbufferNode);
        _graph.Use(_callbackNode);
        _graph.Use(_lightingNode);
        _graph.Use(_volumetricLightNode);
        _graph.Use(_blitNode);

        // Register pipeline-internal counters once; the returned handles are used
        // for zero-allocation PushValue calls on the per-frame hot path.
        _shadowCounter = _profiler.RegisterCounter("Pipeline", "Shadow");
        _gbufferCounter = _profiler.RegisterCounter("Pipeline", "GBuffer");
        _lightingCounter = _profiler.RegisterCounter("Pipeline", "Lighting");
        _volumetricLightCounter = _profiler.RegisterCounter("Pipeline", "VolumetricLight");

        // Create GPU timestamp ring buffer when the device supports it.
        // 4 stages × 2 slots (begin/end) = 8 slots, resolved per-frame with
        // a 3-entry ring buffer for stall-free readback.
        if (_device.TimestampQuerySupported)
        {
            _gpuTimestamps = new GpuTimestampSampler(_device, PipelineTimestampCount, "pbr_pipeline");
        }

        // Volumetric light pass (optional). Created eagerly so no runtime
        // recompilation is needed; controlled at runtime via VolumetricLightEnabled.
        if (volumetricLightShader != null)
        {
            _volumetricLightMaterial = rendering.CreateMaterial(volumetricLightShader);
            _volumetricLightMaterial.DepthStencilState = DepthStencilState.Default;
            _volumetricLightMaterial.RasterizerState = RasterizerState.CullNone;
            _volumetricLightMaterial.BlendState = BlendState.Additive;
            _volumetricLightMaterial.SetBuffer(ShaderResourceId.Data, _lightingDataBuffer);
            _volumetricLightMaterial.SetBuffer(ShaderResourceId.PointLights, _pointLightBuffer);
            _volumetricLightMaterial.SetRenderTextureDepth("_gbufferDepth", _gbufferResource.Texture);
            _volumetricLightMaterial.SetRenderTextureDepth("_shadowMap", _shadowMapResource.Texture);
        }
    }

    /// <summary>
    /// Set the camera used by the pipeline for G-buffer material binding, lighting
    /// data (inverse view-projection, position) and shadow cascade fitting.
    /// The caller must keep the camera updated (e.g. <c>UpdateMatrixToGPU</c>)
    /// before drawing each frame.
    /// </summary>
    /// <param name="camera">The perspective camera buffer.</param>
    public void SetCamera(CameraPerspectiveBuffer camera)
    {
        _camera = camera;
    }

    /// <summary>
    /// Register a render node, dispatched by the interfaces it implements:
    /// <see cref="HbaoRenderer"/> / <see cref="VoxelGiRenderer"/> are registered as
    /// direct render graph nodes between the G-buffer pass and the lighting pass;
    /// <see cref="IGBufferRenderNode"/> / <see cref="IShadowRenderNode"/> nodes are
    /// invoked inside the pipeline's own passes; <see cref="IForwardRenderNode"/> /
    /// <see cref="IContentProcessorNode"/> nodes join the graph's forward/post chain
    /// before the final blit, in registration order. A node may implement both kinds;
    /// a node implementing both chain interfaces runs as a content processor.
    /// The pipeline takes ownership: nodes implementing <see cref="System.IDisposable"/>
    /// are disposed with the pipeline.
    /// </summary>
    /// <exception cref="ArgumentException">The node implements no render node pass interface.</exception>
    public void Use(IRenderNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Direct render graph nodes with transient outputs.
        if (node is HbaoRenderer hbao)
        {
            _aoImport = _graph.CreateTransient(new RenderGraphTextureDescriptor(
                _rendering.PreferredLightMapPass, name: "hbao_ao"));
            hbao.Initialize(this, _aoImport);
            _graph.InsertBefore(_lightingNode, hbao);
            return;
        }
        if (node is VoxelGiRenderer gi)
        {
            _giActive = true;
            _giDiffuseImport = _graph.CreateTransient(new RenderGraphTextureDescriptor(
                _rendering.PreferredLightMapPass, name: "gi_diffuse"));
            _giSpecularImport = _graph.CreateTransient(new RenderGraphTextureDescriptor(
                _rendering.PreferredLightMapPass, name: "gi_specular"));
            gi.Initialize(this, _giDiffuseImport, _giSpecularImport);
            _graph.InsertBefore(_lightingNode, gi);
            return;
        }

        bool isPassNode = node is IGBufferRenderNode or IShadowRenderNode;
        bool isChainNode = node is IForwardRenderNode or IContentProcessorNode;
        if (!isPassNode && !isChainNode)
        {
            throw new ArgumentException(
                $"Render node {node.GetType().Name} implements none of {nameof(IForwardRenderNode)}, {nameof(IContentProcessorNode)}, {nameof(IGBufferRenderNode)}, {nameof(IShadowRenderNode)}.",
                nameof(node));
        }
        if (isPassNode)
        {
            _passNodes.Add(node);
        }
        if (isChainNode)
        {
            IChainAdapterNode adapter;
            // A forward node that is also a direct graph node (e.g. SSR with
            // transient intermediates) registers itself after AttachGraph.
            if (node is ScreenSpaceReflectionRenderer ssr)
            {
                ssr.AttachGraph(_graph);
                adapter = ssr;
            }
            else if (node is IContentProcessorNode processor)
            {
                adapter = new PostProcessNode(this, processor);
            }
            else
            {
                adapter = new ForwardContentNode(this, (IForwardRenderNode)node);
            }
            // The blit node always stays last in the graph.
            _graph.Remove(_blitNode);
            _graph.Use(adapter);
            _graph.Use(_blitNode);
            _chainAdapters.Add(adapter);
        }
    }

    /// <summary>
    /// Unregister a node previously added via <see cref="Use"/>. The node is not disposed.
    /// </summary>
    public bool Remove(IRenderNode node)
    {
        if (node is HbaoRenderer hbao)
        {
            _graph.Remove(hbao);
            if (_aoImport != null)
            {
                _graph.DestroyTransient(_aoImport);
                _aoImport = null;
            }
            return true;
        }
        if (node is VoxelGiRenderer gi)
        {
            _graph.Remove(gi);
            if (_giDiffuseImport != null)
            {
                _graph.DestroyTransient(_giDiffuseImport);
                _giDiffuseImport = null;
            }
            if (_giSpecularImport != null)
            {
                _graph.DestroyTransient(_giSpecularImport);
                _giSpecularImport = null;
            }
            _giActive = false;
            return true;
        }

        bool removed = _passNodes.Remove(node);
        for (int i = 0; i < _chainAdapters.Count; i++)
        {
            if (ReferenceEquals(_chainAdapters[i].Source, node))
            {
                IChainAdapterNode adapter = _chainAdapters[i];
                _graph.Remove(adapter);
                if (adapter is PostProcessNode postProcess)
                {
                    _graph.DestroyTransient(postProcess.Output);
                }
                else if (adapter is ScreenSpaceReflectionRenderer ssr)
                {
                    ssr.DetachGraph();
                }
                _chainAdapters.RemoveAt(i);
                removed = true;
                break;
            }
        }
        return removed;
    }

    /// <summary>
    /// Get the first registered node of the given type (pass nodes, graph nodes,
    /// then the forward chain), or null when the pipeline has none.
    /// </summary>
    public T? Get<T>() where T : class, IRenderNode
    {
        for (int i = 0; i < _passNodes.Count; i++)
        {
            if (_passNodes[i] is T node)
            {
                return node;
            }
        }
        // Check direct graph nodes (HBAO, VoxelGI, SSR, fixed pipeline nodes).
        IReadOnlyList<IRenderGraphNode> graphNodes = _graph.Nodes;
        for (int i = 0; i < graphNodes.Count; i++)
        {
            if (graphNodes[i] is T graphNode)
            {
                return graphNode;
            }
        }
        for (int i = 0; i < _chainAdapters.Count; i++)
        {
            if (_chainAdapters[i].Source is T node)
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Resizes the G-buffer and the forward resolve target. The graph rematerializes
    /// the graph-relative transients at the new size; their facades keep their object
    /// identity, so material bindings need no updates: the affected bind groups are
    /// rebuilt automatically on next use through the render texture version check.
    /// Chain nodes and plugins are notified through the graph's node resize.
    /// <br/>Render bundles recorded against <see cref="GBufferLayout"/> stay valid:
    /// the layout (attachment formats) does not change, only the textures do.
    /// </summary>
    /// <param name="width">The new G-buffer width in pixels.</param>
    /// <param name="height">The new G-buffer height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        if (width == _graph.Width && height == _graph.Height)
        {
            return;
        }

        _graph.Resize(width, height);

        // Plugin output transients and their facades are rematerialized by the
        // graph's own resize. The facades keep their object identity (rebinding
        // through the render texture version check), so no manual refresh is
        // needed here.
    }

    /// <summary>
    /// Execute the complete deferred frame through the render graph: shadow cascades →
    /// G-buffer → <see cref="AfterGBufferCallback"/> → AfterGBuffer plugins → deferred
    /// lighting, resolved into <see cref="ForwardRenderTexture"/> (whose depth is the
    /// G-buffer's own depth attachment) → volumetric light → forward content nodes →
    /// content processors → final blit into <paramref name="destination"/>. Disabled
    /// features and unconsumed work are culled by the graph automatically, and the
    /// whole frame is submitted as a single command batch.
    /// </summary>
    /// <param name="destination">The final output frame buffer (e.g. the swapchain).
    /// When null, the passes and forward content nodes still run but processors are
    /// skipped (minimized or headless view).</param>
    public void Render(GPUFrameBuffer? destination)
    {
        _frameDestinationNull = destination == null;
        _postChain.Current = _sceneColorResource;
        // Start the profiler frame here rather than in the shadow node: with shadows
        // disabled that node is culled, and the counters must still be cleared.
        _profiler.BeginFrame();
        _graph.Execute(destination);

        // Finalize the GPU timestamp sample after all pipeline stages (including
        // volumetric light) have recorded their timestamps.
        _gpuTimestamps?.EndSample();
        _profiler.EndFrame();
    }

    /// <summary>
    /// The GPU buffer holding the point light array. Read by the lighting pass
    /// and GI renderers directly.
    /// </summary>
    public GraphicsBuffer PointLightBuffer => _pointLightBuffer;

    /// <summary>
    /// Upload point lights to the GPU StructuredBuffer. Call once per frame before
    /// <see cref="Render"/>; the active count is tracked internally.
    /// An upload identical to the current contents is skipped (no GPU upload).
    /// </summary>
    /// <param name="lights">Active point lights; excess lights beyond
    /// <see cref="MaxPointLights"/> are silently dropped.</param>
    public void UpdatePointLights(ReadOnlySpan<PointLight> lights)
    {
        int count = Math.Min(lights.Length, MaxPointLights);
        // Compare against the currently uploaded data: identical light arrays
        // skip the GPU upload entirely.
        bool unchanged = count == _pointLightCount
            && MemoryMarshal.AsBytes(lights.Slice(0, count))
                .SequenceEqual(MemoryMarshal.AsBytes(_pointLightBuffer.AsSpan().Slice(0, count)));
        if (unchanged)
        {
            return;
        }

        var span = _pointLightBuffer.AsSpan();
        for (int i = 0; i < count; i++)
        {
            span[i] = lights[i];
        }
        _pointLightBuffer.UpdateBufferRanged(0, (uint)count);
        _pointLightCount = count;
    }

    /// <summary>
    /// Compute cascaded shadow map data for a directional sun: per-cascade light
    /// view-projection matrices, split boundaries and world texel sizes, stored
    /// internally for use by the shadow and lighting passes.
    /// <br/>Splits follow the practical split scheme (log/uniform blend controlled by
    /// <see cref="ShadowSplitLambda"/>) on radial camera distance. The light space is a
    /// pure rotation (camera-independent) and each cascade fits a fixed-radius bounding
    /// sphere of its frustum slice, snapped to texel increments, so the shadow map stays
    /// stable when the camera moves or rotates.
    /// </summary>
    /// <param name="cameraNear">Near boundary of cascade 0, typically the camera near plane distance.</param>
    /// <exception cref="InvalidOperationException">No camera is set (<see cref="SetCamera"/>).</exception>
    public void ComputeShadowCascades(float cameraNear)
    {
        if (_camera == null)
        {
            throw new InvalidOperationException("ComputeShadowCascades requires a camera (call SetCamera first).");
        }

        Matrix4x4.Invert(_camera.Data.ViewProjectionMatrix, out Matrix4x4 invCameraViewProjection);
        Vector3 cameraPosition = _camera.Transform.Position;
        Vector3 sunDirection = SunDirection;
        uint shadowMapSize = ShadowMapSize;
        float shadowDistance = ShadowDistance;
        float casterExtension = ShadowCasterExtension;
        float splitLambda = ShadowSplitLambda;

        // Frustum edge rays: the four far-plane corners in world space.
        Span<Vector3> edgeRays = stackalloc Vector3[4];
        int rayIndex = 0;
        for (int y = -1; y <= 1; y += 2)
        {
            for (int x = -1; x <= 1; x += 2)
            {
                Vector4 corner = Vector4.Transform(new Vector4(x, y, 1.0f, 1.0f), invCameraViewProjection);
                Vector3 farCorner = new Vector3(corner.X, corner.Y, corner.Z) / corner.W;
                edgeRays[rayIndex++] = Vector3.Normalize(farCorner - cameraPosition);
            }
        }

        // Camera-independent light space: a pure rotation around the world origin, so
        // world geometry stays still in light space while the camera moves.
        Vector3 up = Math.Abs(Vector3.Dot(sunDirection, Vector3.UnitZ)) > 0.95f ? Vector3.UnitY : Vector3.UnitZ;
        Matrix4x4 lightView = Matrix4x4.CreateLookAtLeftHanded(Vector3.Zero, sunDirection, up);

        float sliceNear = cameraNear;
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int c = 0; c < ShadowCascadeCount; c++)
        {
            float p = (c + 1) / (float)ShadowCascadeCount;
            float logarithmic = cameraNear * MathF.Pow(shadowDistance / cameraNear, p);
            float uniform = cameraNear + (shadowDistance - cameraNear) * p;
            float sliceFar = splitLambda * logarithmic + (1.0f - splitLambda) * uniform;
            _cascadeSplits[c] = sliceFar;

            // Frustum slice corners on the edge rays.
            Vector3 center = Vector3.Zero;
            for (int r = 0; r < 4; r++)
            {
                corners[r] = cameraPosition + edgeRays[r] * sliceNear;
                corners[r + 4] = cameraPosition + edgeRays[r] * sliceFar;
                center += corners[r] + corners[r + 4];
            }
            center /= 8.0f;

            // Fit a bounding sphere: its radius is invariant to camera rotation and
            // translation, so the texel grid has a constant world size.
            float radius = 0.0f;
            for (int r = 0; r < 8; r++)
            {
                radius = Math.Max(radius, Vector3.Distance(corners[r], center));
            }

            // Grow by one texel per side so the sphere stays inside the snapped box
            // (snapping shifts the box by up to ~0.71 texels diagonally).
            float texel = radius * 2.0f / shadowMapSize;
            radius += texel;
            texel = radius * 2.0f / shadowMapSize;

            // Snap the box center to whole texels so it steps discretely instead of
            // sliding continuously under camera movement.
            Vector3 centerLight = Vector3.Transform(center, lightView);
            centerLight.X = MathF.Floor(centerLight.X / texel) * texel;
            centerLight.Y = MathF.Floor(centerLight.Y / texel) * texel;

            // Depth range: the bounding sphere's Z extent. Do NOT tighten this to the
            // 8 slice corners' min/max Z — the radial-split slice is a spherical shell
            // whose Z extent exceeds the corner hull whenever the light travel direction
            // falls inside the view cone: receivers near the split then project past the
            // ortho far plane, hit the ndc.z > 1 early-out in the lighting shader and
            // are treated as fully lit (a lit band of missing shadow before each split).
            // The near plane extends toward the sun for off-screen casters (negative
            // values are legal for ortho).
            float zMin = centerLight.Z - radius - casterExtension;
            float zMax = centerLight.Z + radius;
            float texelZ = (zMax - zMin) / shadowMapSize;
            zMin = MathF.Floor(zMin / texelZ) * texelZ;
            zMax = zMin + texelZ * shadowMapSize;

            Matrix4x4 ortho = Matrix4x4.CreateOrthographicOffCenterLeftHanded(
                centerLight.X - radius, centerLight.X + radius,
                centerLight.Y - radius, centerLight.Y + radius,
                zMin, zMax);
            _cascadeViewProjections[c] = lightView * ortho;
            _cascadeTexelSizes[c] = texel;

            sliceNear = sliceFar;
        }
    }

    /// <summary>
    /// Writes the (quadrant-folded) light view-projection of one cascade into the
    /// Sets one cascade's light view-projection matrix on the CPU-side shadow data
    /// buffer. The GPU upload is deferred to the caller (see
    /// <see cref="FlushShadowDataBuffer"/>): all four cascade passes set their slot
    /// first, then a single upload fires before any pass is recorded.
    /// </summary>
    internal void SetCascadeViewProjection(int cascadeIndex, in Matrix4x4 viewProjection)
    {
        switch (cascadeIndex)
        {
            case 0: _shadowDataBuffer.Value.CascadeViewProjection0 = viewProjection; break;
            case 1: _shadowDataBuffer.Value.CascadeViewProjection1 = viewProjection; break;
            case 2: _shadowDataBuffer.Value.CascadeViewProjection2 = viewProjection; break;
            default: _shadowDataBuffer.Value.CascadeViewProjection3 = viewProjection; break;
        }
    }

    /// <summary>Uploads the shadow cascade data buffer to the GPU (once per frame).</summary>
    internal void FlushShadowDataBuffer() => _shadowDataBuffer.UpdateBuffer();

    /// <summary>
    /// Convert raw <see cref="Stopwatch"/> ticks to milliseconds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double TicksToMilliseconds(long ticks)
    {
        return (double)ticks / Stopwatch.Frequency * 1000.0;
    }

    /// <summary>
    /// Read back GPU timestamps from the previous sample (guaranteed GPU-complete
    /// via the sampler's throttled interval) and push the values to the profiler.
    /// Called by the G-buffer pass node at the start of its pass.
    /// </summary>
    internal void ReadbackPipelineTimestamps()
    {
        if (_gpuTimestamps == null)
        {
            return;
        }

        ulong[]? timestamps = _gpuTimestamps.TryReadback();
        if (timestamps == null)
        {
            return;
        }

        _profiler.PushValue(_gbufferCounter,
            _gpuTimestamps.DeltaMilliseconds(timestamps, GBufferQueryBase, GBufferQueryBase + 1));
        _profiler.PushValue(_lightingCounter,
            _gpuTimestamps.DeltaMilliseconds(timestamps, LightingQueryBase, LightingQueryBase + 1));
        _profiler.PushValue(_volumetricLightCounter,
            _gpuTimestamps.DeltaMilliseconds(timestamps, VolumetricLightQueryBase, VolumetricLightQueryBase + 1));
    }

    /// <summary>
    /// Assemble <see cref="_lightingData"/> from pipeline properties, camera and
    /// cascade state. Called by the plugin adapter node (so plugins see current
    /// data) and by the lighting node (for the final GPU upload).
    /// </summary>
    internal void AssembleLightingData(Matrix4x4 invViewProjection)
    {
        _lightingData.InvViewProjection = invViewProjection;
        _lightingData.SunViewProjection0 = _cascadeViewProjections[0];
        _lightingData.SunViewProjection1 = _cascadeViewProjections[1];
        _lightingData.SunViewProjection2 = _cascadeViewProjections[2];
        _lightingData.SunViewProjection3 = _cascadeViewProjections[3];
        _lightingData.CameraPosition = new Vector4(_camera!.Transform.Position, 1.0f);
        _lightingData.SunDirection = new Vector4(SunDirection, 0);
        _lightingData.SunColorAndIntensity = new Vector4(SunColor, SunIntensity);
        _lightingData.SkyParams = SkyParams;
        _lightingData.SkyParams2 = SkyParams2;
        _lightingData.SkyHorizonColor = new Vector4(SkyHorizonColor, 0.0f);
        _lightingData.SkyZenithColor = new Vector4(SkyZenithColor, 0.0f);
        _lightingData.Params = new Vector4(
            ShadowEnabled ? 1.0f : 0.0f,
            _pointLightCount,
            ShadowMapSize,
            SunDiscEnabled ? 1.0f : 0.0f);
        _lightingData.CascadeSplits = new Vector4(
            _cascadeSplits[0], _cascadeSplits[1], _cascadeSplits[2], _cascadeSplits[3]);
        _lightingData.CascadeTexelSizes = new Vector4(
            _cascadeTexelSizes[0], _cascadeTexelSizes[1], _cascadeTexelSizes[2], _cascadeTexelSizes[3]);
        _lightingData.Params2 = new Vector4(
            CascadeDebug ? 1.0f : 0.0f,
            ShadowDebug ? 1.0f : 0.0f,
            0.0f,
            AoDebugView ? 1.0f : 0.0f);
        RenderTexture gbuffer = _gbufferResource.Texture;
        _lightingData.ViewportSize = new Vector4(gbuffer.Width, gbuffer.Height, 0, 0);
        _lightingData.Params3 = new Vector4(
            (_giActive && GiEnabled) ? 1.0f : 0.0f,
            GiDiffuseStrength,
            GiSpecularStrength,
            GiDebugView);
        _lightingData.Params4 = new Vector4(SunDiscSize, SunDiscBrightness, 0.0f, 0.0f);
        _lightingData.VLParams = new Vector4(
            VolumetricLightEnabled ? 1.0f : 0.0f,
            VolumetricLightDensity,
            VolumetricLightHeightScale,
            VolumetricLightPhaseG);
    }

    /// <summary>Bind the AO output texture to the lighting material.</summary>
    internal void BindAoOutput(RenderTexture aoTexture)
    {
        _lightingMaterial.SetRenderTexture("_aoTexture", aoTexture);
    }

    /// <summary>Bind the GI diffuse/specular output textures to the lighting material.</summary>
    internal void BindGiOutput(RenderTexture giDiffuse, RenderTexture giSpecular)
    {
        _lightingMaterial.SetRenderTexture("_giDiffuse", giDiffuse);
        _lightingMaterial.SetRenderTexture("_giSpecular", giSpecular);
    }

    private void RebindLightingTargets()
    {
        RenderTexture gbuffer = _gbufferResource.Texture;
        _lightingMaterial.SetRenderTexture("_albedo",   gbuffer, 0);
        _lightingMaterial.SetRenderTexture("_normal",   gbuffer, 1);
        _lightingMaterial.SetRenderTexture("_mrAO",     gbuffer, 2);
        _lightingMaterial.SetRenderTexture("_emissive", gbuffer, 3);
        _lightingMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
        _lightingMaterial.SetRenderTextureDepth("_shadowMap", _shadowMapResource.Texture);
        // Plugin output textures default to white/black until a plugin sets them.
        _lightingMaterial.SetTexture("_aoTexture", _rendering.TextureWhite);
        _lightingMaterial.SetTexture("_giDiffuse", _rendering.TextureBlack);
        _lightingMaterial.SetTexture("_giSpecular", _rendering.TextureBlack);
    }

    // ── Internal surface consumed by the graph nodes (Deferred/Nodes) ──

    internal RenderingSystem Rendering => _rendering;
    internal GPUDevice Device => _device;
    internal Mesh FullScreenMesh => _fullScreenMesh;
    internal CameraPerspectiveBuffer? Camera => _camera;
    internal List<IRenderNode> PassNodes => _passNodes;
    internal GraphicsMaterial LightingMaterial => _lightingMaterial;
    internal GraphicsMaterial? VolumetricLightMaterial => _volumetricLightMaterial;
    internal RenderContext ShadowPassContext => _shadowContext;
    internal RenderContext GBufferPassContext => _gbufferContext;
    internal RenderContext LightingPassContext => _lightingContext;
    internal RenderContext VolumetricLightPassContext => _volumetricLightContext;
    internal GpuTimestampSampler? GpuTimestamps => _gpuTimestamps;
    internal RenderProfileCounterId ShadowCounter => _shadowCounter;
    internal RenderProfileCounterId GBufferCounter => _gbufferCounter;
    internal RenderProfileCounterId LightingCounter => _lightingCounter;
    internal RenderProfileCounterId VolumetricLightCounter => _volumetricLightCounter;
    internal GraphicsValueBuffer<DeferredLightingData> LightingDataBufferTyped => _lightingDataBuffer;
    internal DeferredLightingData CurrentLightingData => _lightingData;
    internal Matrix4x4[] CascadeViewProjections => _cascadeViewProjections;
    internal RenderGraph Graph => _graph;
    internal GPUAttachmentLayout PostProcessLayout => _postProcessLayout;
    internal RenderGraphTexture GBufferResource => _gbufferResource;
    internal RenderGraphTexture ShadowMapResource => _shadowMapResource;
    internal RenderGraphTexture SceneColorResource => _sceneColorResource;
    internal PostChainState PostChain => _postChain;
    internal bool FrameDestinationNull => _frameDestinationNull;
    internal RenderGraphTexture? AoImport => _aoImport;
    internal RenderGraphTexture? GiDiffuseImport => _giDiffuseImport;
    internal RenderGraphTexture? GiSpecularImport => _giSpecularImport;
    internal bool HasAfterGBufferCallback => AfterGBufferCallback != null;
    internal void InvokeAfterGBufferCallback() => AfterGBufferCallback?.Invoke();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _passNodes.Clear();
            // Disposes every registered node (fixed nodes, chain adapters — which
            // dispose their source nodes — and the blit node), the transient
            // facades and the texture pool.
            _graph.Dispose();
            _shadowContext.Dispose();
            _gbufferContext.Dispose();
            _lightingContext.Dispose();
            _volumetricLightContext.Dispose();
            _lightingDataBuffer.Dispose();
            _shadowDataBuffer.Dispose();
            _pointLightBuffer.Dispose();
            _lightingMaterial.Dispose();
            _volumetricLightMaterial?.Dispose();
            _gbufferLayout.Dispose();
            _shadowLayout.Dispose();
            _forwardLayout.Dispose();
            _postProcessLayout.Dispose();
            _gpuTimestamps?.Dispose();
        }
    }
}
