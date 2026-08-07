using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A deferred PBR rendering pipeline built on the engine's WebGPU resources.
/// <br/>Owns a G-buffer (albedo / normal / metallic-roughness-ao / emissive + depth), a
/// depth-only shadow map holding <see cref="ShadowCascadeCount"/> cascades in a 2x2 atlas,
/// three render contexts (shadow pass, G-buffer pass, lighting pass) and the pass-private
/// deferred lighting material. G-buffer scene draws and shadow scene draws are handled
/// by externally owned scene renderers (<see cref="GBufferRenderer"/> /
/// <see cref="ShadowRenderer"/>) registered via <see cref="AddSceneRenderer"/>; the
/// pipeline does not know their types — it invokes <see cref="ISceneRenderer"/> methods
/// inside each pass. The caller can also drive passes manually via Begin/End methods.
/// <br/>The caller drives the frame by calling the convenience methods
/// <see cref="RenderShadowPass"/> + <see cref="RenderGBufferPass"/>, then
/// <see cref="ExecutePlugins"/> + <see cref="RenderLighting(target)"/> which resolves
/// lighting, sky and shadows into the target frame buffer (typically the engine's HDR
/// main target). Each pass can also be driven manually via Begin/End methods.
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
/// <br/>Pluggable effects (AO, GI, etc.) implementing <see cref="IRenderPlugin"/> can be
/// registered via <see cref="RegisterPlugin"/>; they execute at their declared
/// <see cref="RenderInjectionPoint"/> and their output textures are bound to the
/// lighting material automatically.
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

    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly Mesh _fullScreenMesh;

    // External scene renderers (GBufferRenderer, ShadowRenderer, etc.) invoked
    // inside the pipeline's own passes. Each renderer only overrides the passes
    // it cares about; unimplemented passes are no-ops via default interface methods.
    private readonly List<ISceneRenderer> _sceneRenderers = new();

    private readonly GPUAttachmentLayout _gbufferLayout;
    private readonly GPUAttachmentLayout _shadowLayout;
    private RenderTexture _gbufferRT;
    private readonly RenderTexture _shadowRT;

    // Pipeline-internal forward RT: HDR color + depth. Lighting and forward
    // transparency both render into here, then a final blit copies the result
    // to the caller's target. This lets forward glass use hardware depth
    // testing (the depth is pre-filled from the G-buffer via a native CopyTexture).
    private readonly GPUAttachmentLayout _forwardLayout;
    private RenderTexture _forwardRT;
    private readonly GraphicsMaterial _compositeMaterial;
    private readonly RenderContext _compositeContext;
    private readonly GPUCommandBuffer _depthCopyCommand;

    private readonly GraphicsMaterial _lightingMaterial;
    private CameraPerspectiveBuffer? _camera;

    private readonly GraphicsValueBuffer<DeferredLightingData> _lightingDataBuffer;
    private readonly GraphicsValueBuffer<ShadowCascadeData> _shadowDataBuffer;
    private readonly GraphicsArrayBuffer<PointLight> _pointLightBuffer;

    // Pluggable render effects (AO, GI, etc.) executed between the G-buffer
    // and lighting passes. The pipeline binds their output textures to the
    // lighting material automatically after execution.
    private readonly List<IRenderPlugin> _plugins = new();

    /// <summary>
    /// Called inside <see cref="Render"/> after the G-buffer pass, before AfterGBuffer
    /// plugins. Use this to submit per-frame dynamic data (e.g. voxel GI instances).
    /// </summary>
    public event Action? AfterGBufferCallback;

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
    private readonly Stopwatch _frameStopwatch = new();
    private long _shadowElapsedTicks;
    private long _stageStartTicks;

    // GPU timestamp ring buffer for per-stage GPU timing. Each stage (G-buffer /
    // lighting) owns 2 query slots (begin + end) in a shared query set. The ring
    // buffer provides per-frame readback with a 2-frame latency, no stalls.
    private const int PipelineTimestampCount = 6; // 3 stages × 2 (begin/end)
    private const int ShadowQueryBase = 0;
    private const int GBufferQueryBase = 2;
    private const int LightingQueryBase = 4;
    private GpuTimestampSampler? _gpuTimestamps;

    private readonly RenderContext _shadowContext;
    private readonly RenderContext _gbufferContext;
    private readonly RenderContext _lightingContext;
    private readonly RenderContext _forwardContext;

    /// <summary>
    /// The G-buffer render texture (albedo+packed-roughness /
    /// detail+packed-geometric normal / metallic-roughness-ao /
    /// emissive+packed-geometric normal / depth).
    /// </summary>
    public RenderTexture GBuffer => _gbufferRT;

    /// <summary>
    /// The depth-only shadow map render texture (a 2x2 cascade atlas).
    /// </summary>
    public RenderTexture ShadowMap => _shadowRT;

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
    public float SunDiscSize { get; set; } = 0.9998f;

    /// <summary>Sun disc HDR visual brightness (independent of lighting intensity).</summary>
    public float SunDiscBrightness { get; set; } = 18.0f;

    /// <summary>Atmosphere params: x=rayleighScale, y=mieScale, z=miePhaseG, w=exposure.</summary>
    public Vector4 SkyParams { get; set; } = new(1.0f, 1.0f, 0.8f, 1.0f);

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
    /// The G-buffer render texture (albedo / normal / mrAO / emissive + depth).
    /// The forward renderer reads its depth attachment for manual depth testing.
    /// </summary>
    public RenderTexture GBufferRenderTexture => _gbufferRT;

    /// <summary>
    /// The cascaded shadow map render texture. The forward renderer reads it for
    /// shadow comparison sampling.
    /// </summary>
    public RenderTexture ShadowRenderTexture => _shadowRT;

    /// <summary>
    /// The live G-buffer render context for immediate (per-frame dynamic) draws.
    /// Only valid between <see cref="BeginGBufferPass"/> and <see cref="EndGBufferPass"/>.
    /// </summary>
    public IRenderContext GBufferContext => _gbufferContext;

    /// <summary>
    /// The live shadow render context for immediate (per-frame dynamic) draws.
    /// Only valid between <see cref="BeginShadowPass"/> and <see cref="EndShadowPass"/>.
    /// </summary>
    public IRenderContext ShadowContext => _shadowContext;

    /// <summary>
    /// Register a pluggable render effect. The pipeline executes the plugin at
    /// its declared <see cref="RenderInjectionPoint"/> and binds the output
    /// textures to the lighting material automatically. The caller owns the
    /// plugin's lifetime (dispose it after disposing the pipeline or
    /// unregistering it).
    /// </summary>
    /// <param name="plugin">The render plugin to register.</param>
    public void RegisterPlugin(IRenderPlugin plugin)
    {
        _plugins.Add(plugin);
        // Track whether any GI plugin is registered so the lighting shader can
        // gate the GI code path. The flag is updated on register/unregister.
        if (plugin is VoxelGiRenderer)
        {
            _giActive = true;
        }
    }

    /// <summary>
    /// Unregister a previously registered render plugin.
    /// </summary>
    public void UnregisterPlugin(IRenderPlugin plugin)
    {
        _plugins.Remove(plugin);
        if (plugin is VoxelGiRenderer)
        {
            _giActive = _plugins.Any(p => p is VoxelGiRenderer);
        }
    }

    /// <summary>
    /// Get the first registered plugin of the specified type, or null.
    /// </summary>
    public T? GetPlugin<T>() where T : class, IRenderPlugin
    {
        for (int i = 0; i < _plugins.Count; i++)
        {
            if (_plugins[i] is T typed)
            {
                return typed;
            }
        }
        return null;
    }

    /// <summary>
    /// Execute all plugins registered at the given injection point and bind
    /// their output textures to the lighting material. Called by the caller
    /// between <see cref="EndGBufferPass"/> and <see cref="RenderLighting"/>.
    /// The context (camera, G-buffer, shadow map, lighting data, point-light
    /// buffer) is assembled internally from pipeline state.
    /// </summary>
    /// <exception cref="InvalidOperationException">No camera is set.</exception>
    public void ExecutePlugins(RenderInjectionPoint point)
    {
        if (_camera == null)
        {
            throw new InvalidOperationException("ExecutePlugins requires a camera (call SetCamera first).");
        }

        Matrix4x4.Invert(_camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);

        // Pre-populate the lighting data for plugins that need it (GI reads sun
        // direction, cascades, sky colors from here).
        AssembleLightingData(invViewProjection);

        RenderPluginContext context = new()
        {
            Rendering = _rendering,
            GBuffer = _gbufferRT,
            ShadowMap = _shadowRT,
            InvViewProjection = invViewProjection,
            ProjectionMatrix = _camera.Data.ProjectionMatrix,
            CameraTransform = _camera.Transform,
            Width = _gbufferRT.Width,
            Height = _gbufferRT.Height,
            LightingData = _lightingData,
            PointLightBuffer = _pointLightBuffer,
            Profiler = Profiler,
        };

        for (int i = 0; i < _plugins.Count; i++)
        {
            IRenderPlugin plugin = _plugins[i];
            if (plugin.InjectionPoint == point)
            {
                plugin.Execute(context);
            }
        }
        RebindPluginOutputs(context);
    }

    /// <summary>
    /// Convert raw <see cref="Stopwatch"/> ticks to milliseconds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double TicksToMilliseconds(long ticks)
    {
        return (double)ticks / Stopwatch.Frequency * 1000.0;
    }

    /// <summary>
    /// Read back GPU timestamps from the previous sample (0.5s ago, guaranteed
    /// GPU-complete via the throttled timer) and push the values to the profiler.
    /// Called at the start of each frame's G-buffer pass.
    /// </summary>
    private void ReadbackPipelineTimestamps()
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
    }

    /// <summary>
    /// Assemble <see cref="_lightingData"/> from pipeline properties, camera and
    /// cascade state. Called by both <see cref="ExecutePlugins"/> (so plugins see
    /// current data) and <see cref="RenderLighting"/> (for the final GPU upload).
    /// </summary>
    private void AssembleLightingData(Matrix4x4 invViewProjection)
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
        _lightingData.ViewportSize = new Vector4(_gbufferRT.Width, _gbufferRT.Height, 0, 0);
        _lightingData.Params3 = new Vector4(
            (_giActive && GiEnabled) ? 1.0f : 0.0f,
            GiDiffuseStrength,
            GiSpecularStrength,
            GiDebugView);
        _lightingData.Params4 = new Vector4(SunDiscSize, SunDiscBrightness, 0.0f, 0.0f);
    }

    private void RebindPluginOutputs(RenderPluginContext context)
    {
        if (context.AOResult != null)
        {
            _lightingMaterial.SetRenderTexture("_aoTexture", context.AOResult);
        }
        else
        {
            _lightingMaterial.SetTexture("_aoTexture", _rendering.TextureWhite);
        }

        if (context.GIDiffuse != null)
        {
            _lightingMaterial.SetRenderTexture("_giDiffuse", context.GIDiffuse);
            _lightingMaterial.SetRenderTexture("_giSpecular", context.GISpecular!);
        }
        else
        {
            _lightingMaterial.SetTexture("_giDiffuse", _rendering.TextureBlack);
            _lightingMaterial.SetTexture("_giSpecular", _rendering.TextureBlack);
        }
    }

    /// <summary>
    /// Create the deferred PBR pipeline.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="lightingShaderText">The source text of the deferred lighting shader (DeferredLighting.hlsl).</param>
    /// <param name="lightingShaderName">The name of the deferred lighting shader.</param>
    /// <param name="shadowMapSize">The per-cascade shadow map resolution in texels; the shadow map is a 2x2 atlas of <see cref="ShadowCascadeCount"/> cascades, so the actual texture is twice this size along each axis.</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    public PBRDeferredPipeline(
        RenderingSystem rendering,
        string lightingShaderText,
        string lightingShaderName,
        string blitShaderText,
        uint shadowMapSize = 2048,
        uint width = 1280,
        uint height = 720)
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

        _gbufferRT = rendering.CreateRenderTexture(_gbufferLayout, width, height, "pbr_gbuffer");

        _shadowLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_shadow_pass"));

        // 2x2 cascade atlas: each cascade renders into one quadrant.
        _shadowRT = rendering.CreateRenderTexture(_shadowLayout, shadowMapSize * 2, shadowMapSize * 2, "pbr_shadow_map");

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
        _forwardContext = rendering.CreateRenderContext("pbr_forward_pass");

        // Forward RT: HDR color + Depth32Float (must match the G-buffer depth format
        // for native CopyTexture, which requires copy-compatible formats).
        _forwardLayout = _device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(rendering.PreferredHDRFormat)],
            new DepthAttachment(PixelFormat.Depth32Float),
            "pbr_forward_pass"));
        _forwardRT = rendering.CreateRenderTexture(_forwardLayout, width, height, "pbr_forward");

        // Composite material: blit forward RT color to the caller's target.
        Shader blitShader = rendering.CreateShader(
            blitShaderText, "Shaders/Pipelines/Utils/Blit.hlsl");
        _compositeMaterial = rendering.CreateMaterial(blitShader);
        _compositeMaterial.DepthStencilState = DepthStencilState.Default;
        _compositeMaterial.RasterizerState = RasterizerState.CullNone;
        _compositeMaterial.SetRenderTexture("_texture", _forwardRT, 0);

        // Dedicated command buffer for the native G-buffer → forward RT depth copy.
        _depthCopyCommand = _device.CreateCommandBuffer("pbr_depth_copy");

        _compositeContext = rendering.CreateRenderContext("pbr_composite");

        // Register pipeline-internal counters once; the returned handles are used
        // for zero-allocation PushValue calls on the per-frame hot path.
        _shadowCounter = _profiler.RegisterCounter("Pipeline", "Shadow");
        _gbufferCounter = _profiler.RegisterCounter("Pipeline", "GBuffer");
        _lightingCounter = _profiler.RegisterCounter("Pipeline", "Lighting");

        // Create GPU timestamp ring buffer when the device supports it.
        // 3 stages × 2 slots (begin/end) = 6 slots, resolved per-frame with
        // a 3-entry ring buffer for stall-free readback.
        if (_device.TimestampQuerySupported)
        {
            _gpuTimestamps = new GpuTimestampSampler(_device, PipelineTimestampCount, "pbr_pipeline");
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
    /// Register a scene renderer (e.g. <see cref="GBufferRenderer"/>,
    /// <see cref="ShadowRenderer"/>) that draws objects into the pipeline's own
    /// passes. The renderer only overrides the passes it cares about; the pipeline
    /// invokes it inside each pass between Begin and End.
    /// </summary>
    public void AddSceneRenderer(ISceneRenderer renderer)
    {
        _sceneRenderers.Add(renderer);
    }

    /// <summary>
    /// Unregister a scene renderer previously added via <see cref="AddSceneRenderer"/>.
    /// </summary>
    public void RemoveSceneRenderer(ISceneRenderer renderer)
    {
        _sceneRenderers.Remove(renderer);
    }

    /// <summary>
    /// Recreate the G-buffer at a new resolution. Call when the view resizes.
    /// <br/>Render bundles recorded against <see cref="GBufferLayout"/> stay valid:
    /// the layout (attachment formats) does not change, only the textures do.
    /// </summary>
    /// <param name="width">The new G-buffer width in pixels.</param>
    /// <param name="height">The new G-buffer height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        _gbufferRT.Dispose();
        _gbufferRT = _rendering.CreateRenderTexture(_gbufferLayout, width, height, "pbr_gbuffer");

        // Recreate the pipeline-internal forward RT at the new resolution.
        _forwardRT.Dispose();
        _forwardRT = _rendering.CreateRenderTexture(_forwardLayout, width, height, "pbr_forward");

        for (int i = 0; i < _plugins.Count; i++)
        {
            _plugins[i].Resize(width, height);
        }
        RebindLightingTargets();

        // Rebind composite material to the new forward RT.
        _compositeMaterial.SetRenderTexture("_texture", _forwardRT, 0);
    }

    /// <summary>
    /// Execute the complete deferred frame in the correct order: shadow cascades →
    /// G-buffer → <see cref="AfterGBufferCallback"/> → AfterGBuffer plugins → deferred
    /// lighting → forward transparency. This is the recommended single-call entry
    /// point; the individual <c>Render*Pass</c> methods remain available for custom
    /// pipelines or debugging.
    /// </summary>
    /// <param name="target">The HDR frame buffer to resolve lighting and blend
    /// transparent objects into (typically the engine's main frame buffer).</param>
    public void Render(GPUFrameBuffer target)
    {
        RenderShadowPass();
        RenderGBufferPass();
        AfterGBufferCallback?.Invoke();
        ExecutePlugins(RenderInjectionPoint.AfterGBuffer);
        RenderLighting(_forwardRT.FrameBuffer);
        RenderForwardPass(_forwardRT.FrameBuffer);
        CompositeToTarget(target);
    }

    /// <summary>
    /// The GPU buffer holding the point light array. Passed to plugins via
    /// <see cref="RenderPluginContext"/> automatically by <see cref="ExecutePlugins"/>.
    /// </summary>
    public GraphicsBuffer PointLightBuffer => _pointLightBuffer;

    /// <summary>
    /// Upload point lights to the GPU StructuredBuffer. Call once per frame before
    /// <see cref="RenderLighting"/>; the active count is tracked internally.
    /// </summary>
    /// <param name="lights">Active point lights; excess lights beyond
    /// <see cref="MaxPointLights"/> are silently dropped.</param>
    public void UpdatePointLights(ReadOnlySpan<PointLight> lights)
    {
        int count = Math.Min(lights.Length, MaxPointLights);
        var span = _pointLightBuffer.AsSpan();
        for (int i = 0; i < count; i++)
        {
            span[i] = lights[i];
        }
        _pointLightBuffer.UpdateBufferRanged(0, (uint)count);
        _pointLightCount = count;
    }

    /// <summary>
    /// Begin the shadow map pass for one cascade. All shadow draws must happen
    /// between this and <see cref="EndShadowPass"/>: bundle replays via
    /// <see cref="ExecuteShadowSubContext"/> and/or immediate draws via
    /// <see cref="ShadowContext"/>. Cascades render into their own quadrant of
    /// the 2x2 atlas; only the first cascade's pass clears the atlas.
    /// <br/>The light view-projection matrix is read from the cascade data computed
    /// by <see cref="ComputeShadowCascades"/>.
    /// </summary>
    /// <param name="cascadeIndex">The cascade to render (0 = nearest .. <see cref="ShadowCascadeCount"/>-1).</param>
    public void BeginShadowPass(int cascadeIndex)
    {
        // The shadow pass is the first GPU work of each frame; start the profiler
        // frame here and begin measuring the total shadow duration.
        if (cascadeIndex == 0)
        {
            _profiler.BeginFrame();
            _shadowElapsedTicks = 0;
        }
        _stageStartTicks = Stopwatch.GetTimestamp();

        // Fold the atlas quadrant into the projection. The scissor is essential:
        // geometry outside this cascade's orthographic box can otherwise transform
        // into another atlas quadrant and corrupt that cascade's depth values.
        float offsetX = (cascadeIndex % 2) - 0.5f;
        float offsetY = 0.5f - (cascadeIndex / 2);
        Matrix4x4 quadrant = Matrix4x4.CreateScale(0.5f, 0.5f, 1.0f) * Matrix4x4.CreateTranslation(offsetX, offsetY, 0.0f);
        SetCascadeViewProjection(cascadeIndex, _cascadeViewProjections[cascadeIndex] * quadrant);
        _shadowContext.Begin(_shadowRT.FrameBuffer, clearDepth: cascadeIndex == 0 ? 1.0f : null);
        _shadowContext.SetScissorRect(
            (uint)(cascadeIndex % 2) * ShadowMapSize,
            (uint)(cascadeIndex / 2) * ShadowMapSize,
            ShadowMapSize,
            ShadowMapSize);
    }

    private void SetCascadeViewProjection(int cascadeIndex, in Matrix4x4 viewProjection)
    {
        // All four cascade passes record before their command buffers are submitted,
        // so every slot holds this frame's value when the passes execute on the GPU.
        switch (cascadeIndex)
        {
            case 0: _shadowDataBuffer.Value.CascadeViewProjection0 = viewProjection; break;
            case 1: _shadowDataBuffer.Value.CascadeViewProjection1 = viewProjection; break;
            case 2: _shadowDataBuffer.Value.CascadeViewProjection2 = viewProjection; break;
            default: _shadowDataBuffer.Value.CascadeViewProjection3 = viewProjection; break;
        }
        _shadowDataBuffer.UpdateBuffer();
    }

    /// <summary>
    /// Replay a recorded shadow render bundle. Must be called inside the shadow pass
    /// (the pass applies its scissor rect, which bundles cannot set themselves).
    /// </summary>
    /// <param name="subContext">The recorded sub render context.</param>
    public void ExecuteShadowSubContext(SubRenderContext subContext)
    {
        _shadowContext.ExecuteSubContext(subContext);
    }

    /// <summary>
    /// End the shadow map pass and submit its commands.
    /// </summary>
    public void EndShadowPass()
    {
        _shadowContext.End();
        _shadowElapsedTicks += Stopwatch.GetTimestamp() - _stageStartTicks;
    }

    /// <summary>
    /// Convenience: run all <see cref="ShadowCascadeCount"/> cascade passes in one
    /// call, invoking the registered shadow render callback between Begin/End for
    /// each cascade. When no callback is registered each cascade's pass runs empty.
    /// </summary>
    public void RenderShadowPass()
    {
        for (int c = 0; c < ShadowCascadeCount; c++)
        {
            BeginShadowPass(c);
            for (int i = 0; i < _sceneRenderers.Count; i++)
            {
                _sceneRenderers[i].OnRenderShadow(_shadowContext, c);
            }
            EndShadowPass();
        }
    }

    /// <summary>
    /// Compute cascaded shadow map data for a directional sun: per-cascade light
    /// view-projection matrices, split boundaries and world texel sizes, stored
    /// internally for use by <see cref="BeginShadowPass"/> and <see cref="RenderLighting"/>.
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

            // Depth range: use the actual min/max Z of the 8 frustum-slice
            // corners in light space instead of the bounding-sphere diameter,
            // which reclaims wasted depth precision (directly reducing acne
            // for a given bias). Extend the near plane toward the sun for
            // off-screen casters (negative values are legal for ortho).
            float zMin = float.MaxValue;
            float zMax = float.MinValue;
            for (int r = 0; r < 8; r++)
            {
                Vector3 cornerLight = Vector3.Transform(corners[r], lightView);
                zMin = Math.Min(zMin, cornerLight.Z);
                zMax = Math.Max(zMax, cornerLight.Z);
            }
            zMin -= casterExtension;

            // Quantize on a grid derived from the (stable) sphere radius so
            // the depth sampling stays consistent across frames.
            float texelZ = (radius * 2.0f + casterExtension) / shadowMapSize;
            zMin = MathF.Floor(zMin / texelZ) * texelZ;
            zMax = zMin + texelZ * (float)Math.Ceiling((zMax - zMin) / texelZ);

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
    /// Begin the G-buffer pass. All G-buffer draws must happen between this and
    /// <see cref="EndGBufferPass"/>: bundle replays via <see cref="ExecuteGBufferSubContext"/>
    /// and/or immediate draws via <see cref="GBufferContext"/>.
    /// </summary>
    public void BeginGBufferPass()
    {
        _profiler.PushValue(_shadowCounter, TicksToMilliseconds(_shadowElapsedTicks));
        _stageStartTicks = Stopwatch.GetTimestamp();

        // Read back GPU timestamps from 2 frames ago (ring buffer guarantees
        // GPU completion) and push them to the profiler.
        ReadbackPipelineTimestamps();

        ReadOnlySpan<ClearColorData> clearColors = stackalloc ClearColorData[4]
        {
            new(0, Vector4.Zero),
            new(1, new Vector4(0.5f, 0.5f, 1.0f, 1.0f)),
            new(2, Vector4.Zero),
            new(3, Vector4.Zero),
        };
        if (_gpuTimestamps != null && _gpuTimestamps.ShouldRecord)
        {
            _gbufferContext.Begin(_gbufferRT.FrameBuffer, clearColors,
                _gpuTimestamps.QuerySet, GBufferQueryBase, GBufferQueryBase + 1, 1.0f);
        }
        else
        {
            _gbufferContext.Begin(_gbufferRT.FrameBuffer, clearColors, 1.0f);
        }
    }

    /// <summary>
    /// Replay a recorded G-buffer render bundle. Must be called inside the G-buffer pass.
    /// </summary>
    /// <param name="subContext">The recorded sub render context.</param>
    public void ExecuteGBufferSubContext(SubRenderContext subContext)
    {
        _gbufferContext.ExecuteSubContext(subContext);
    }

    /// <summary>
    /// End the G-buffer pass and submit its commands.
    /// </summary>
    public void EndGBufferPass()
    {
        if (_gpuTimestamps != null && _gpuTimestamps.ShouldRecord)
        {
            _gbufferContext.ResolveTimestampsOnEnd(
                _gpuTimestamps.QuerySet, GBufferQueryBase, 2, _gpuTimestamps.ResolveBuffer);
        }
        _gbufferContext.End();
        _profiler.PushValue(_gbufferCounter, TicksToMilliseconds(Stopwatch.GetTimestamp() - _stageStartTicks));
    }

    /// <summary>
    /// Convenience: begin the G-buffer pass, invoke the registered G-buffer render
    /// callback (if any), then end the pass — all in one call. When no callback is
    /// registered this is equivalent to calling Begin/End with nothing in between.
    /// </summary>
    public void RenderGBufferPass()
    {
        BeginGBufferPass();
        for (int i = 0; i < _sceneRenderers.Count; i++)
        {
            _sceneRenderers[i].OnRenderGBuffer(_gbufferContext, _gbufferLayout);
        }
        EndGBufferPass();
    }

    /// <summary>
    /// Render transparent objects in a forward pass onto the given target (the
    /// pipeline-internal forward RT after deferred lighting). First copies the
    /// G-buffer depth into the forward RT so transparent objects are depth-tested
    /// against opaque geometry by hardware. The existing color content is preserved.
    /// Each registered scene renderer's <see cref="ISceneRenderer.OnRenderForward"/>
    /// is called inside the pass.
    /// </summary>
    /// <param name="target">The frame buffer to blend transparent objects onto.</param>
    public void RenderForwardPass(GPUFrameBuffer target)
    {
        // Skip the entire context Begin/End when no scene renderer has forward content.
        bool hasForward = false;
        for (int i = 0; i < _sceneRenderers.Count; i++)
        {
            if (_sceneRenderers[i].HasForwardContent)
            {
                hasForward = true;
                break;
            }
        }
        if (!hasForward)
        {
            return;
        }

        // Copy G-buffer depth into the forward RT via native CopyTexture so glass
        // materials can use hardware depth testing (DepthStencilState.Read) instead
        // of a manual shader-side discard.
        _depthCopyCommand.Begin();
        _depthCopyCommand.CopyTexture(
            _gbufferRT.FrameBuffer.DepthStencil!,
            target.DepthStencil!,
            0, 0, TextureAspect.All);
        _depthCopyCommand.End();
        _rendering.ScheduleCommandBuffer(_depthCopyCommand);

        _forwardContext.Begin(target);
        for (int i = 0; i < _sceneRenderers.Count; i++)
        {
            _sceneRenderers[i].OnRenderForward(_forwardContext, target.AttachmentLayout);
        }
        _forwardContext.End();
    }

    /// <summary>
    /// Blit the pipeline-internal forward RT color onto the caller's target frame buffer.
    /// </summary>
    /// <param name="target">The caller's frame buffer (e.g. the engine's main HDR target).</param>
    private void CompositeToTarget(GPUFrameBuffer target)
    {
        _compositeContext.Begin(target);
        _compositeContext.Draw(_fullScreenMesh, _compositeMaterial);
        _compositeContext.End();
    }

    /// <summary>
    /// Resolve lighting, shadows and the sky into the target frame buffer
    /// (typically the engine's HDR main target). Assembles the GPU constant buffer
    /// from caller-set properties, camera data and internally tracked cascade state.
    /// </summary>
    /// <param name="target">The frame buffer to render the lighting result into.</param>
    /// <exception cref="InvalidOperationException">No camera is set.</exception>
    public void RenderLighting(GPUFrameBuffer target)
    {
        if (_camera == null)
        {
            throw new InvalidOperationException("RenderLighting requires a camera (call SetCamera first).");
        }

        long lightingStart = Stopwatch.GetTimestamp();

        Matrix4x4.Invert(_camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);
        AssembleLightingData(invViewProjection);

        _lightingDataBuffer.UpdateBuffer(_lightingData);
        bool recordGpu = _gpuTimestamps != null && _gpuTimestamps.ShouldRecord;
        if (recordGpu)
        {
            _lightingContext.Begin(target, ReadOnlySpan<ClearColorData>.Empty,
                _gpuTimestamps!.QuerySet, LightingQueryBase, LightingQueryBase + 1);
        }
        else
        {
            _lightingContext.Begin(target);
        }
        _lightingContext.Draw(_fullScreenMesh, _lightingMaterial);
        if (recordGpu)
        {
            _lightingContext.ResolveTimestampsOnEnd(
                _gpuTimestamps!.QuerySet, LightingQueryBase, 2, _gpuTimestamps.ResolveBuffer);
        }
        _lightingContext.End();

        _profiler.PushValue(_lightingCounter, TicksToMilliseconds(Stopwatch.GetTimestamp() - lightingStart));

        _gpuTimestamps?.EndSample();
        _profiler.EndFrame();
    }

    private void RebindLightingTargets()
    {
        _lightingMaterial.SetRenderTexture("_albedo",   _gbufferRT, 0);
        _lightingMaterial.SetRenderTexture("_normal",   _gbufferRT, 1);
        _lightingMaterial.SetRenderTexture("_mrAO",     _gbufferRT, 2);
        _lightingMaterial.SetRenderTexture("_emissive", _gbufferRT, 3);
        _lightingMaterial.SetRenderTextureDepth("_gbufferDepth", _gbufferRT);
        _lightingMaterial.SetRenderTextureDepth("_shadowMap", _shadowRT);
        // Plugin output textures default to white/black until a plugin sets them.
        _lightingMaterial.SetTexture("_aoTexture", _rendering.TextureWhite);
        _lightingMaterial.SetTexture("_giDiffuse", _rendering.TextureBlack);
        _lightingMaterial.SetTexture("_giSpecular", _rendering.TextureBlack);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shadowContext.Dispose();
            _gbufferContext.Dispose();
            _lightingContext.Dispose();
            _forwardContext.Dispose();
            _lightingDataBuffer.Dispose();
            _shadowDataBuffer.Dispose();
            _pointLightBuffer.Dispose();
            _lightingMaterial.Dispose();
            _gbufferRT.Dispose();
            _shadowRT.Dispose();
            _gbufferLayout.Dispose();
            _shadowLayout.Dispose();
            _gpuTimestamps?.Dispose();
        }
    }
}
