using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;

using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Volumetric clouds renderer for deferred PBR compositions: a half-resolution
/// ray-marched cloud slab (Perlin-Worley base + Worley-detail erosion, Nubis
/// style — see <c>Shaders/Pipelines/Rendering/PBR/VolumetricClouds.slang</c>)
/// composited over the HDR scene color with a depth-aware bilateral upsample,
/// plus a small cloud-shadow coverage bake the deferred lighting pass uses to
/// dim the direct sun, so cloud shadows drift across the terrain.
/// <br/>The cloud lighting adapts to the procedural sky automatically: the sun
/// energy is the atmosphere's own solar radiance attenuated at cloud height
/// (sunset clouds stay lit and redden after the ground light has faded), and
/// the ambient is the CPU-filtered sky gradient — both arrive through the
/// shared lighting data buffer, so no sky state is duplicated here.
/// <br/>The 3D noise textures (128³ base + 32³ detail, RGBA8) are generated
/// once by <c>VolumetricCloudNoise.slang</c> on the first frame. The shadow
/// coverage texture is node-owned (not a graph transient): the lighting pass
/// reads the previous frame's bake, one frame behind the visible clouds but
/// always in lockstep with its own uniforms.
/// <br/>Attach the renderer via <see cref="Attach"/>: it creates its transient
/// march target, registers itself directly after the deferred lighting node
/// (before the volumetric light overlay, whose near-camera shafts correctly
/// add over the composited clouds), binds the coverage texture to the lighting
/// material's <c>_cloudShadow</c> slot and publishes the shadow uniforms on the
/// scene environment.
/// </summary>
public sealed class RGNode_VolumetricClouds : AutoDisposable, IRenderGraphNode
{
    /// <summary>
    /// Per-frame cloud data uploaded to the march, composite and shadow bake
    /// passes. Layout must match the <c>_cloudData</c> cbuffer in
    /// VolumetricClouds.slang / VolumetricCloudsComposite.slang /
    /// VolumetricCloudShadow.slang exactly.
    /// </summary>
    private struct VolumetricCloudsData
    {
        /// <summary>x=coverage y=density multiplier z=bottom altitude km w=slab thickness km.</summary>
        public Vector4 CloudParams;
        /// <summary>x=detailStrength y=extinction 1/km z=march resolution scale w=max march steps.</summary>
        public Vector4 CloudParams2;
        /// <summary>xy=accumulated wind offset km z=accumulated time s w=detail drift phase.</summary>
        public Vector4 CloudWind;
        /// <summary>x=ambient strength y=sun strength z=aerial fade start km w=aerial fade end km.</summary>
        public Vector4 CloudLight;
        /// <summary>x=opacity debug view y=shadow bake half extent km zw=shadow bake center (world xz, km).</summary>
        public Vector4 CloudDebug;
    }

    private const int BaseNoiseSize = 128;
    private const int DetailNoiseSize = 32;
    private const int ShadowCoverageSize = 256;
    private const float DetailDriftSpeed = 0.012f; // detail uvw units per second (200 m texture period)

    // GPU timestamp slots: two per timed stage (begin + end). The one-time noise
    // bake is not measured.
    private const int ShadowBakeQueryBase = 0;
    private const int MarchQueryBase = 2;
    private const int CompositeQueryBase = 4;
    private const int TimestampSlotCount = 6;

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly GraphicsMaterial _marchMaterial;
    private readonly GraphicsMaterial _compositeMaterial;
    private readonly ComputeMaterial _noiseBaseMaterial;
    private readonly ComputeMaterial _noiseDetailMaterial;
    private readonly ComputeMaterial _shadowBakeMaterial;
    private readonly GraphicsValueBuffer<VolumetricCloudsData> _dataBuffer;
    private readonly Texture3D _baseNoise;
    private readonly Texture3D _detailNoise;
    private readonly Texture2D _shadowCoverage;
    private readonly Mesh _fullScreenMesh;

    // Graph state; null until Attach.
    private RenderGraph? _graph;
    private RenderChain? _chain;
    private RGNode_DeferredLighting? _lighting;
    private PBRSceneEnvironment? _environment;
    private RenderGraphTexture? _gbufferResource;
    private RenderGraphTexture? _marchResource;

    // The chain content composited this frame, captured during Setup.
    private RenderGraphTexture? _compositeTarget;

    // Facade rebind caches (recreated on resize).
    private RenderTexture? _boundGBuffer;
    private RenderTexture? _boundMarchTarget;

    // Accumulated animation state.
    private long _lastFrameTicks;
    private float _windOffsetX;
    private float _windOffsetY;
    private float _timeSeconds;
    private bool _noiseBaked;

    private bool _isEnabled = true;
    private float _shadowStrength = 0.55f;

    // Profiler counter handle — lazily registered on first Execute.
    private RenderProfileCounterId _shadowBakeGpuCounter;
    private RenderProfileCounterId _marchGpuCounter;
    private RenderProfileCounterId _compositeGpuCounter;
    private bool _profilerCounterRegistered;

    // Per-stage GPU timing (throttled sampler, 6 slots) and the cached durations
    // re-pushed to the profiler every frame (its BeginFrame clears the buffers).
    private readonly GpuTimestampSampler? _gpuTimestamps;
    private double _shadowBakeGpuMilliseconds;
    private double _marchGpuMilliseconds;
    private double _compositeGpuMilliseconds;

    /// <summary>Cloud coverage (0 = clear sky, 1 = overcast). Slides the
    /// coverage window over the noise field, growing existing clouds before
    /// closing the gaps.</summary>
    public float Coverage { get; set; } = 0.42f;

    /// <summary>Density multiplier of the cloud medium.</summary>
    public float Density { get; set; } = 1.0f;

    /// <summary>Altitude of the cloud slab bottom, kilometers.</summary>
    public float BottomAltitudeKm { get; set; } = 1.6f;

    /// <summary>Thickness of the cloud slab, kilometers.</summary>
    public float ThicknessKm { get; set; } = 3.4f;

    /// <summary>Silhouette erosion strength from the high-frequency detail
    /// noise (0 = smooth billows, 1 = shredded edges).</summary>
    public float DetailStrength { get; set; } = 0.38f;

    /// <summary>Cloud extinction coefficient at density 1, per kilometer
    /// (higher = more opaque, darker bases).</summary>
    public float ExtinctionPerKm { get; set; } = 16.0f;

    /// <summary>Wind heading in degrees (+X = 0, +Z = 90).</summary>
    public float WindDirectionDeg { get; set; } = 35.0f;

    /// <summary>Wind speed in meters per second (drifts the whole field).</summary>
    public float WindSpeed { get; set; } = 8.0f;

    /// <summary>Ambient (sky) illumination strength on the clouds.</summary>
    public float AmbientStrength { get; set; } = 1.0f;

    /// <summary>Sun illumination strength on the clouds (scales the
    /// atmosphere's solar radiance driver).</summary>
    public float SunStrength { get; set; } = 0.8f;

    /// <summary>Distance where clouds start dissolving into the horizon sky,
    /// kilometers.</summary>
    public float AerialFadeStartKm { get; set; } = 14.0f;

    /// <summary>Distance where clouds have fully dissolved, kilometers.</summary>
    public float AerialFadeEndKm { get; set; } = 30.0f;

    /// <summary>March step budget per pixel (quality knob: 64 fast … 160
    /// ultra). Empty-space skipping means typical rays use far fewer; the
    /// default is the performance tier.</summary>
    public int MaxMarchSteps { get; set; } = 64;

    /// <summary>The march pass resolution scale relative to the graph
    /// viewport (0.5 = half resolution).</summary>
    public float MarchResolutionScale { get; set; } = 0.5f;

    /// <summary>Cloud shadow strength on the direct sun (0 = off).</summary>
    public float ShadowStrength
    {
        get => _shadowStrength;
        set
        {
            _shadowStrength = value;
            SyncShadowEnvironment();
        }
    }

    /// <summary>Half extent of the shadow coverage window around the camera,
    /// kilometers.</summary>
    public float ShadowExtentKm { get; set; } = 20.0f;

    /// <summary>Visualize the march opacity (grayscale) instead of compositing.</summary>
    public bool DebugOpacityView { get; set; }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            SyncShadowEnvironment();
        }
    }

    /// <summary>
    /// Creates the volumetric clouds renderer with its three shaders. The 3D
    /// noise textures are created here and baked by a compute dispatch on the
    /// first <see cref="Execute"/>; no GPU work is submitted eagerly.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="marchShader">The cloud march shader (VolumetricClouds.slang).</param>
    /// <param name="compositeShader">The composite shader (VolumetricCloudsComposite.slang).</param>
    /// <param name="noiseShader">The noise bake compute shader (VolumetricCloudNoise.slang).</param>
    /// <param name="shadowShader">The shadow coverage bake compute shader (VolumetricCloudShadow.slang).</param>
    public RGNode_VolumetricClouds(
        RenderingSystem rendering,
        Shader marchShader,
        Shader compositeShader,
        Shader noiseShader,
        Shader shadowShader)
    {
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _fullScreenMesh = rendering.MeshFullScreen;

        GPUSampler noiseSampler = _device.GetSampler(FilterMode.Linear, AddressMode.Repeat);
        _baseNoise = rendering.CreateTexture3D(
            BaseNoiseSize, BaseNoiseSize, BaseNoiseSize, PixelFormat.RGBA8Unorm, 1,
            TextureUsage.TextureBinding | TextureUsage.StorageBinding, noiseSampler, "cloud_base_noise");
        _detailNoise = rendering.CreateTexture3D(
            DetailNoiseSize, DetailNoiseSize, DetailNoiseSize, PixelFormat.RGBA8Unorm, 1,
            TextureUsage.TextureBinding | TextureUsage.StorageBinding, noiseSampler, "cloud_detail_noise");

        // Camera-centered shadow coverage map (sampled by the lighting pass
        // with a clamp-to-edge linear sampler).
        var shadowTextureDescriptor = new TextureDescriptor(
            TextureDimension.Texture2D, PixelFormat.RGBA8Unorm,
            ShadowCoverageSize, ShadowCoverageSize, 1, 1,
            TextureUsage.TextureBinding | TextureUsage.StorageBinding, 1, "cloud_shadow_coverage");
        GPUTexture shadowTexture = _device.CreateTexture(shadowTextureDescriptor);
        GPUTextureView shadowView = _device.CreateTextureView(new TextureViewDescriptor(
            shadowTexture, TextureViewDimension.Texture2D, 0, 1, name: "cloud_shadow_coverage_view"));
        _shadowCoverage = rendering.CreateTexture2D(
            shadowTexture, shadowView,
            _device.GetSampler(FilterMode.Linear, AddressMode.ClampToEdge),
            ownsResources: true);

        _dataBuffer = rendering.CreateGraphicsValueBuffer<VolumetricCloudsData>("volumetric_clouds_data");

        _noiseBaseMaterial = rendering.CreateComputeMaterial(noiseShader);
        _noiseBaseMaterial.SetTexture3DStorage("_noiseOut", _baseNoise, 0);
        _noiseDetailMaterial = rendering.CreateComputeMaterial(noiseShader, ["NOISE_DETAIL"]);
        _noiseDetailMaterial.SetTexture3DStorage("_noiseOut", _detailNoise, 0);
        _shadowBakeMaterial = rendering.CreateComputeMaterial(shadowShader);
        _shadowBakeMaterial.SetBuffer("_cloudData", _dataBuffer);
        _shadowBakeMaterial.SetTexture("_cloudBaseNoise", _baseNoise);
        _shadowBakeMaterial.SetTexture2DStorage("_shadowOut", _shadowCoverage, 0);

        _marchMaterial = rendering.CreateMaterial(marchShader);
        _marchMaterial.DepthStencilState = DepthStencilState.Default;
        _marchMaterial.RasterizerState = RasterizerState.CullNone;
        _marchMaterial.SetBuffer("_cloudData", _dataBuffer);
        _marchMaterial.SetTexture3D("_cloudBaseNoise", _baseNoise);
        _marchMaterial.SetTexture3D("_cloudDetailNoise", _detailNoise);

        _compositeMaterial = rendering.CreateMaterial(compositeShader);
        _compositeMaterial.DepthStencilState = DepthStencilState.Default;
        _compositeMaterial.RasterizerState = RasterizerState.CullNone;
        _compositeMaterial.BlendState = BlendState.PremultipliedAlpha;
        _compositeMaterial.SetBuffer("_cloudData", _dataBuffer);

        if (_device.TimestampQuerySupported)
        {
            _gpuTimestamps = new GpuTimestampSampler(_device, TimestampSlotCount, "volumetric_clouds");
        }
    }

    /// <summary>
    /// Attaches the renderer to a deferred composition: creates the transient
    /// half-resolution march target, registers itself directly after the
    /// lighting node (before the volumetric light overlay), wires the shared
    /// lighting data / G-buffer depth / shadow map bindings, binds the shadow
    /// coverage texture to the lighting material's _cloudShadow slot and
    /// publishes the cloud shadow uniforms on the environment.
    /// </summary>
    /// <param name="graph">The render graph driving the frame.</param>
    /// <param name="chain">The content chain whose current content the clouds
    /// composite over (the scene color at this point in the frame).</param>
    /// <param name="lighting">The deferred lighting node the clouds follow.</param>
    /// <param name="gbuffer">The G-buffer resource (shared depth for the march
    /// clamping and the bilateral weights).</param>
    /// <param name="shadowMap">The shadow map resource (declared by the shared
    /// PBR common include; not sampled by the march itself).</param>
    /// <param name="environment">The shared scene environment (camera access
    /// and the cloud shadow uniforms).</param>
    /// <exception cref="InvalidOperationException">The renderer is already attached.</exception>
    public void Attach(
        RenderGraph graph,
        RenderChain chain,
        RGNode_DeferredLighting lighting,
        RenderGraphTexture gbuffer,
        RenderGraphTexture shadowMap,
        PBRSceneEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentNullException.ThrowIfNull(gbuffer);
        ArgumentNullException.ThrowIfNull(shadowMap);
        ArgumentNullException.ThrowIfNull(environment);
        if (_graph != null)
        {
            throw new InvalidOperationException("The volumetric clouds renderer is already attached (call Detach first).");
        }
        _graph = graph;
        _chain = chain;
        _lighting = lighting;
        _environment = environment;
        _gbufferResource = gbuffer;
        _marchResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, resolutionScale: MarchResolutionScale, name: "volumetric_clouds_march"));

        RenderTexture gbufferFacade = gbuffer.Texture;
        _marchMaterial.SetRenderTextureDepth("_gbufferDepth", gbufferFacade);
        _compositeMaterial.SetRenderTextureDepth("_gbufferDepth", gbufferFacade);
        _boundGBuffer = gbufferFacade;

        // The march and composite passes include PBRCommon.slang, whose
        // reflection may declare the shared lighting cbuffer, the point-light
        // storage buffer and the shadow map. The cbuffer is always sampled;
        // the other two may be dead-code-eliminated, so bind them
        // opportunistically.
        _marchMaterial.SetBuffer(ShaderResourceId.Data, environment.LightingDataBuffer);
        _marchMaterial.TrySetBuffer(ShaderResourceId.PointLights, environment.PointLightBuffer);
        _marchMaterial.TrySetRenderTextureDepth("_shadowMap", shadowMap.Texture);
        _compositeMaterial.SetBuffer(ShaderResourceId.Data, environment.LightingDataBuffer);
        _compositeMaterial.TrySetBuffer(ShaderResourceId.PointLights, environment.PointLightBuffer);
        _compositeMaterial.TrySetRenderTextureDepth("_shadowMap", shadowMap.Texture);

        lighting.Material.SetTexture("_cloudShadow", _shadowCoverage);
        SyncShadowEnvironment();

        graph.InsertAfter(lighting, this);
    }

    /// <summary>
    /// Detaches the renderer from the graph: unregisters it, destroys its
    /// transient resource, restores the lighting material's _cloudShadow
    /// fallback and zeroes the environment's cloud shadow uniforms. The
    /// renderer can be re-attached afterwards.
    /// </summary>
    public void Detach()
    {
        if (_graph == null)
        {
            return;
        }
        _graph.Remove(this);
        if (_marchResource != null)
        {
            _graph.DestroyTransient(_marchResource);
            _marchResource = null;
        }
        if (_lighting != null)
        {
            _lighting.Material.SetTexture("_cloudShadow", _rendering.TextureWhite);
            _lighting = null;
        }
        if (_environment != null)
        {
            _environment.CloudShadowStrength = 0.0f;
            _environment = null;
        }
        _compositeTarget = null;
        _boundGBuffer = null;
        _boundMarchTarget = null;
        _graph = null;
        _chain = null;
        _gbufferResource = null;
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _compositeTarget = _chain!.Current!;
        builder.Read(_gbufferResource!);
        builder.Write(_marchResource!);
        builder.ReadWrite(_compositeTarget);
        if (!_graph!.HasDestinationThisFrame)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        CameraPerspectiveBuffer? camera = _environment!.Camera;
        if (camera == null)
        {
            throw new InvalidOperationException(
                "Volumetric clouds require a camera (set the environment's Camera first).");
        }

        long now = Stopwatch.GetTimestamp();
        float deltaSeconds = _lastFrameTicks == 0
            ? 0.0f
            : MathF.Min((now - _lastFrameTicks) / (float)Stopwatch.Frequency, 0.25f);
        _lastFrameTicks = now;

        // Accumulate the wind offset on the CPU so the shader coordinates stay
        // small and precise however long the session runs.
        float heading = WindDirectionDeg * MathF.PI / 180.0f;
        float windStepKm = WindSpeed * deltaSeconds * 0.001f;
        _windOffsetX += MathF.Cos(heading) * windStepKm;
        _windOffsetY += MathF.Sin(heading) * windStepKm;
        _timeSeconds += deltaSeconds;

        Vector3 cameraPosition = camera.Transform.Position;
        VolumetricCloudsData data = new()
        {
            CloudParams = new Vector4(Coverage, Density, BottomAltitudeKm, ThicknessKm),
            CloudParams2 = new Vector4(DetailStrength, ExtinctionPerKm, MarchResolutionScale, MaxMarchSteps),
            CloudWind = new Vector4(_windOffsetX, _windOffsetY, _timeSeconds, _timeSeconds * DetailDriftSpeed),
            CloudLight = new Vector4(AmbientStrength, SunStrength, AerialFadeStartKm, AerialFadeEndKm),
            CloudDebug = new Vector4(
                DebugOpacityView ? 1.0f : 0.0f,
                ShadowExtentKm,
                cameraPosition.X * 0.001f,
                cameraPosition.Y * 0.001f),
        };
        _dataBuffer.UpdateBuffer(data);

        // Publish the shadow uniforms the lighting pass reads next frame (it
        // assembles its data before this node runs), in lockstep with the
        // coverage texture baked below: altitude and extent follow the live
        // slab properties.
        _environment.CloudShadowPlaneAltitude = (BottomAltitudeKm + ThicknessKm * 0.5f) * 1000.0f;
        _environment.CloudShadowExtent = ShadowExtentKm * 2000.0f;
        SyncShadowEnvironment();

        // The G-buffer and march facades are recreated on resize; rebind only then.
        RenderTexture gbuffer = _gbufferResource!.Texture;
        RenderTexture marchTarget = _marchResource!.Texture;
        if (!ReferenceEquals(_boundGBuffer, gbuffer))
        {
            _marchMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _compositeMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _boundGBuffer = gbuffer;
        }
        if (!ReferenceEquals(_boundMarchTarget, marchTarget))
        {
            _compositeMaterial.SetRenderTexture("_clouds", marchTarget, 0);
            _boundMarchTarget = marchTarget;
        }

        GPUCommandBuffer commandBuffer = context.RenderContext.CommandBuffer;

        bool measureGpu = _gpuTimestamps != null && _gpuTimestamps.ShouldRecord;

        // One-time 3D noise bake (dispatched into the frame's command buffer so
        // no out-of-band submission is needed; the textures persist afterwards).
        if (!_noiseBaked)
        {
            using (GPUCommandBuffer.ComputePass noisePass = commandBuffer.BeginCompute())
            {
                _noiseBaseMaterial.DispatchBySize(noisePass, BaseNoiseSize, BaseNoiseSize, BaseNoiseSize);
                _noiseDetailMaterial.DispatchBySize(noisePass, DetailNoiseSize, DetailNoiseSize, DetailNoiseSize);
            }
            _noiseBaked = true;
        }

        // Cloud shadow coverage bake around the camera (read by the lighting
        // pass next frame).
        using (GPUCommandBuffer.ComputePass shadowPass = measureGpu
            ? commandBuffer.BeginCompute(_gpuTimestamps!.QuerySet, ShadowBakeQueryBase, ShadowBakeQueryBase + 1)
            : commandBuffer.BeginCompute())
        {
            _shadowBakeMaterial.DispatchBySize(shadowPass, ShadowCoverageSize, ShadowCoverageSize, 1);
        }

        // Half-resolution cloud march, then the full-resolution composite over
        // the chain's current content.
        using (RenderPassScope marchPass = measureGpu
            ? context.RenderContext.BeginPass(marchTarget.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                _gpuTimestamps!.QuerySet, MarchQueryBase, MarchQueryBase + 1)
            : context.RenderContext.BeginPass(marchTarget.FrameBuffer))
        {
            marchPass.Draw(_fullScreenMesh, _marchMaterial);
        }
        using (RenderPassScope compositePass = measureGpu
            ? context.RenderContext.BeginPass(_compositeTarget!.Texture.ColorFrameBuffer, ReadOnlySpan<ClearColorData>.Empty,
                _gpuTimestamps!.QuerySet, CompositeQueryBase, CompositeQueryBase + 1)
            : context.RenderContext.BeginPass(_compositeTarget!.Texture.ColorFrameBuffer))
        {
            compositePass.Draw(_fullScreenMesh, _compositeMaterial);
            if (measureGpu)
            {
                // Resolve the whole slot range once the final pass closes.
                compositePass.ResolveTimestampsOnEnd(
                    _gpuTimestamps!.QuerySet, 0, TimestampSlotCount, _gpuTimestamps.ResolveBuffer);
            }
        }

        // Lazily register the GPU counters; the cached GPU durations are re-pushed
        // every frame (BeginFrame cleared the buffers). The readback below is
        // synchronous but reads the previous sample — the recorded resolves have
        // not executed yet (submission happens at frame end).
        RenderProfiler? profiler = _graph!.Profiler;
        if (profiler != null && !_profilerCounterRegistered)
        {
            if (_gpuTimestamps != null)
            {
                _shadowBakeGpuCounter = profiler.RegisterCounter("VolumetricClouds", "Shadow Bake (GPU)");
                _marchGpuCounter = profiler.RegisterCounter("VolumetricClouds", "March (GPU)");
                _compositeGpuCounter = profiler.RegisterCounter("VolumetricClouds", "Composite (GPU)");
            }
            _profilerCounterRegistered = true;
        }

        if (measureGpu)
        {
            ulong[]? timestamps = _gpuTimestamps!.TryReadback();
            if (timestamps != null)
            {
                _shadowBakeGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, ShadowBakeQueryBase, ShadowBakeQueryBase + 1);
                _marchGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, MarchQueryBase, MarchQueryBase + 1);
                _compositeGpuMilliseconds = _gpuTimestamps.DeltaMilliseconds(timestamps, CompositeQueryBase, CompositeQueryBase + 1);
            }
            _gpuTimestamps.EndSample();
        }

        if (profiler != null && _gpuTimestamps != null)
        {
            profiler.PushValue(_shadowBakeGpuCounter, _shadowBakeGpuMilliseconds);
            profiler.PushValue(_marchGpuCounter, _marchGpuMilliseconds);
            profiler.PushValue(_compositeGpuCounter, _compositeGpuMilliseconds);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _baseNoise.Dispose();
            _detailNoise.Dispose();
            _shadowCoverage.Dispose();
            _dataBuffer.Dispose();
            _gpuTimestamps?.Dispose();
        }
    }

    // The lighting pass reads the shadow uniforms one frame after this node
    // wrote them; keeping the strength the only switch means a disabled node
    // fades the shadows out atomically with the composite.
    private void SyncShadowEnvironment()
    {
        if (_environment == null)
        {
            return;
        }
        _environment.CloudShadowStrength = _isEnabled ? _shadowStrength : 0.0f;
    }
}
