using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Debug views exposed by <see cref="RadianceCacheRenderer"/>.
/// </summary>
public enum RadianceCacheDebugMode
{
    /// <summary>Render the normally lit scene.</summary>
    Off,
    /// <summary>Show diffuse irradiance from the radiance cache.</summary>
    DiffuseIrradiance,
    /// <summary>Show cache-based indirect specular radiance.</summary>
    IndirectSpecular,
    /// <summary>Show cache coverage and temporal confidence.</summary>
    CacheConfidence,
}

/// <summary>
/// Shader set required by <see cref="RadianceCacheRenderer"/>.
/// </summary>
public readonly struct RadianceCacheShaders
{
    /// <summary>Per-frame accumulation-buffer clear shader.</summary>
    public required Shader Clear { get; init; }
    /// <summary>Screen-surface injection shader.</summary>
    public required Shader Inject { get; init; }
    /// <summary>Cache reprojection and temporal update shader.</summary>
    public required Shader Update { get; init; }
    /// <summary>World-space radiance propagation shader.</summary>
    public required Shader Propagate { get; init; }
    /// <summary>Half-resolution final-gather shader.</summary>
    public required Shader Trace { get; init; }
    /// <summary>Full-resolution bilateral and temporal resolve shader.</summary>
    public required Shader Resolve { get; init; }
}

/// <summary>
/// Runtime statistics for <see cref="RadianceCacheRenderer"/>.
/// </summary>
public readonly struct RadianceCacheStatistics
{
    /// <summary>Total world-space cache cells across all cascades.</summary>
    public int CacheCellCount { get; }
    /// <summary>Estimated memory owned by cache buffers and GI textures.</summary>
    public long MemoryBytes { get; }
    /// <summary>CPU time used to record the most recent cache update.</summary>
    public double CpuRecordMilliseconds { get; }

    /// <summary>Create a radiance-cache statistics snapshot.</summary>
    public RadianceCacheStatistics(int cacheCellCount, long memoryBytes, double cpuRecordMilliseconds)
    {
        CacheCellCount = cacheCellCount;
        MemoryBytes = memoryBytes;
        CpuRecordMilliseconds = cpuRecordMilliseconds;
    }
}

/// <summary>
/// A screen-seeded, cascaded world-space radiance-cache GI plugin.
/// <br/>Visible G-buffer surfaces inject directly lit outgoing radiance into
/// three camera-following grids. The grids are reprojected when they scroll,
/// retain off-screen entries, propagate radiance into nearby empty cells and
/// feed their previous result back into injection for converged multi-bounce
/// diffuse transport. A small screen-space gather supplies near-field detail,
/// while the persistent cache supplies stable off-screen lighting.
/// <br/>The implementation owns no voxel-GI resources and can be selected or
/// disposed independently of <see cref="VoxelGiRenderer"/>.
/// </summary>
public sealed class RadianceCacheRenderer : AutoDisposable, IGlobalIlluminationPlugin
{
    private const int CascadeCount = 3;

    private struct RadianceCacheData
    {
        public Matrix4x4 InvViewProjection;
        public Matrix4x4 ViewProjection;
        public Matrix4x4 ViewProjectionPrev;
        public Matrix4x4 SunViewProjection0;
        public Matrix4x4 SunViewProjection1;
        public Matrix4x4 SunViewProjection2;
        public Matrix4x4 SunViewProjection3;
        public Vector4 CameraPosition;
        public Vector4 PreviousCameraPosition;
        public Vector4 SunDirection;
        public Vector4 SunColorAndIntensity;
        public Vector4 SkyHorizonColor;
        public Vector4 SkyZenithColor;
        public Vector4 CascadeSplits;
        public Vector4 CascadeTexelSizes;
        public Vector4 CacheOrigin0;
        public Vector4 CacheOrigin1;
        public Vector4 CacheOrigin2;
        public Vector4 PreviousCacheOrigin0;
        public Vector4 PreviousCacheOrigin1;
        public Vector4 PreviousCacheOrigin2;
        public Vector4 CacheParams;
        public Vector4 ViewportParams;
        public Vector4 LightingParams;
        public Vector4 ResponseParams;
        public Vector4 TraceParams;
    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _clearMaterial;
    private readonly ComputeMaterial _injectMaterial;
    private readonly ComputeMaterial _updateMaterial;
    private readonly ComputeMaterial _propagateMaterial;
    private readonly ComputeMaterial _traceMaterial;
    private readonly ComputeMaterial _resolveMaterial;
    private readonly GraphicsValueBuffer<RadianceCacheData> _dataBuffer;
    private readonly GraphicsArrayBuffer<Vector4>[] _cacheRadiance = new GraphicsArrayBuffer<Vector4>[2];
    private readonly GraphicsArrayBuffer<Vector4>[] _cacheGeometry = new GraphicsArrayBuffer<Vector4>[2];
    private readonly GraphicsBuffer _accumRadiance;
    private readonly GraphicsBuffer _accumNormal;
    private readonly Vector4[] _cacheOrigins = new Vector4[CascadeCount];
    private readonly Vector4[] _previousCacheOrigins = new Vector4[CascadeCount];
    private readonly int _gridResolution;
    private readonly int _cacheCellCount;
    private readonly float _baseCellSize;

    private RenderTexture _diffuseRaw;
    private RenderTexture _specularRaw;
    private readonly RenderTexture[] _diffuseHistory = new RenderTexture[2];
    private readonly RenderTexture[] _specularHistory = new RenderTexture[2];
    private RenderTexture? _boundGBuffer;
    private RenderTexture? _boundShadowMap;
    private GraphicsBuffer? _boundPointLights;
    private uint _gbufferWidth;
    private uint _gbufferHeight;
    private float _traceResolutionScale;
    private uint _frameIndex;
    private int _historyReadIndex;
    private bool _historyValid;
    private Matrix4x4 _previousViewProjection = Matrix4x4.Identity;
    private Vector3 _previousCameraPosition;
    private long _memoryBytes;
    private RenderProfileCounterId _profileCounter;
    private bool _profileCounterRegistered;

    /// <summary>Gets or sets the diffuse contribution from recursively cached lighting.</summary>
    public float BounceStrength { get; set; } = 0.85f;

    /// <summary>Gets or sets cache history retention for cells updated this frame.</summary>
    public float CacheHysteresis { get; set; } = 0.94f;

    /// <summary>Gets or sets full-resolution temporal resolve history retention.</summary>
    public float TemporalHysteresis { get; set; } = 0.88f;

    /// <summary>Gets or sets retained energy for cache cells not visible this frame.</summary>
    public float OffscreenRetention { get; set; } = 0.9975f;

    /// <summary>Gets or sets the strength of the per-frame cache propagation step.</summary>
    public float PropagationStrength { get; set; } = 0.42f;

    /// <summary>Gets or sets the screen-space near-field search distance in world units.</summary>
    public float TraceMaxDistance { get; set; } = 16.0f;

    /// <summary>Gets or sets the physical-sky irradiance multiplier.</summary>
    public float SkyIntensity { get; set; } = 1.0f;

    /// <summary>Gets or sets the emissive-material injection multiplier.</summary>
    public float EmissiveScale { get; set; } = 1.0f;

    /// <summary>Gets or sets the active GI debug view.</summary>
    public RadianceCacheDebugMode DebugView { get; set; }

    /// <summary>Gets or sets the half-resolution gather scale in the range [0.25, 1].</summary>
    public float TraceResolutionScale
    {
        get => _traceResolutionScale;
        set
        {
            ValidateTraceResolutionScale(value);
            if (MathF.Abs(value - _traceResolutionScale) < 0.0001f)
            {
                return;
            }
            _traceResolutionScale = value;
            Resize(_gbufferWidth, _gbufferHeight);
        }
    }

    /// <summary>Gets the current full-resolution diffuse GI texture.</summary>
    public RenderTexture DiffuseTexture => _diffuseHistory[_historyReadIndex];

    /// <summary>Gets the current full-resolution specular GI texture.</summary>
    public RenderTexture SpecularTexture => _specularHistory[_historyReadIndex];

    /// <summary>Gets statistics from the most recent frame.</summary>
    public RadianceCacheStatistics Statistics { get; private set; }

    /// <inheritdoc />
    public string Name => "RadianceCacheGI";

    /// <inheritdoc />
    public RenderInjectionPoint InjectionPoint => RenderInjectionPoint.AfterGBuffer;

    /// <summary>
    /// Create a cascaded radiance-cache GI plugin.
    /// </summary>
    /// <param name="rendering">Rendering system used to allocate GPU resources.</param>
    /// <param name="shaders">Complete radiance-cache compute shader set.</param>
    /// <param name="width">Initial G-buffer width.</param>
    /// <param name="height">Initial G-buffer height.</param>
    /// <param name="gridResolution">Cells per axis in each of the three cascades.</param>
    /// <param name="baseCellSize">World-space cell size of the finest cascade.</param>
    /// <param name="traceResolutionScale">Final-gather resolution relative to the G-buffer.</param>
    public RadianceCacheRenderer(
        RenderingSystem rendering,
        RadianceCacheShaders shaders,
        uint width,
        uint height,
        int gridResolution = 32,
        float baseCellSize = 0.5f,
        float traceResolutionScale = 0.5f)
    {
        if (gridResolution < 16 || gridResolution > 64 || (gridResolution & (gridResolution - 1)) != 0)
        {
            throw new ArgumentException("The cache grid resolution must be a power of two in [16, 64].", nameof(gridResolution));
        }
        if (!(baseCellSize > 0.0f) || !float.IsFinite(baseCellSize))
        {
            throw new ArgumentOutOfRangeException(nameof(baseCellSize));
        }
        ValidateTraceResolutionScale(traceResolutionScale);

        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _gridResolution = gridResolution;
        _baseCellSize = baseCellSize;
        _cacheCellCount = checked(gridResolution * gridResolution * gridResolution * CascadeCount);
        _gbufferWidth = Math.Max(width, 1u);
        _gbufferHeight = Math.Max(height, 1u);
        _traceResolutionScale = traceResolutionScale;

        _commandBuffer = _device.CreateCommandBuffer("radiance_cache_gi");
        _clearMaterial = rendering.CreateComputeMaterial(shaders.Clear);
        _injectMaterial = rendering.CreateComputeMaterial(shaders.Inject);
        _updateMaterial = rendering.CreateComputeMaterial(shaders.Update);
        _propagateMaterial = rendering.CreateComputeMaterial(shaders.Propagate);
        _traceMaterial = rendering.CreateComputeMaterial(shaders.Trace);
        _resolveMaterial = rendering.CreateComputeMaterial(shaders.Resolve);
        _dataBuffer = rendering.CreateGraphicsValueBuffer<RadianceCacheData>("radiance_cache_data");

        uint cacheBytes = checked((uint)(_cacheCellCount * 16));
        _cacheRadiance[0] = rendering.CreateGraphicsArrayBuffer(_cacheCellCount, Vector4.Zero, "radiance_cache_radiance_a");
        _cacheRadiance[1] = rendering.CreateGraphicsArrayBuffer(_cacheCellCount, Vector4.Zero, "radiance_cache_radiance_b");
        _cacheGeometry[0] = rendering.CreateGraphicsArrayBuffer(_cacheCellCount, Vector4.Zero, "radiance_cache_geometry_a");
        _cacheGeometry[1] = rendering.CreateGraphicsArrayBuffer(_cacheCellCount, Vector4.Zero, "radiance_cache_geometry_b");
        _accumRadiance = rendering.CreateGraphicsBuffer(cacheBytes, "radiance_cache_accum_radiance");
        _accumNormal = rendering.CreateGraphicsBuffer(cacheBytes, "radiance_cache_accum_normal");

        BindStaticResources();
        uint traceWidth = TraceWidth(_gbufferWidth);
        uint traceHeight = TraceHeight(_gbufferHeight);
        _diffuseRaw = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth, traceHeight, "radiance_cache_diffuse_raw");
        _specularRaw = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth, traceHeight, "radiance_cache_specular_raw");
        _diffuseHistory[0] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, _gbufferWidth, _gbufferHeight, "radiance_cache_diffuse_a");
        _diffuseHistory[1] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, _gbufferWidth, _gbufferHeight, "radiance_cache_diffuse_b");
        _specularHistory[0] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, _gbufferWidth, _gbufferHeight, "radiance_cache_specular_a");
        _specularHistory[1] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, _gbufferWidth, _gbufferHeight, "radiance_cache_specular_b");
        BindResolutionResources();
        UpdateMemoryEstimate();
    }

    private void BindStaticResources()
    {
        _clearMaterial.SetBuffer("_data", _dataBuffer);
        _clearMaterial.SetBuffer("_accumRadiance", _accumRadiance);
        _clearMaterial.SetBuffer("_accumNormal", _accumNormal);

        _injectMaterial.SetBuffer("_data", _dataBuffer);
        _injectMaterial.SetBuffer("_cacheRadiance", _cacheRadiance[0]);
        _injectMaterial.SetBuffer("_accumRadiance", _accumRadiance);
        _injectMaterial.SetBuffer("_accumNormal", _accumNormal);

        _updateMaterial.SetBuffer("_data", _dataBuffer);
        _updateMaterial.SetBuffer("_accumRadiance", _accumRadiance);
        _updateMaterial.SetBuffer("_accumNormal", _accumNormal);
        _updateMaterial.SetBuffer("_cacheRadianceIn", _cacheRadiance[0]);
        _updateMaterial.SetBuffer("_cacheGeometryIn", _cacheGeometry[0]);
        _updateMaterial.SetBuffer("_cacheRadianceOut", _cacheRadiance[1]);
        _updateMaterial.SetBuffer("_cacheGeometryOut", _cacheGeometry[1]);

        _propagateMaterial.SetBuffer("_data", _dataBuffer);
        _propagateMaterial.SetBuffer("_cacheRadianceIn", _cacheRadiance[1]);
        _propagateMaterial.SetBuffer("_cacheGeometryIn", _cacheGeometry[1]);
        _propagateMaterial.SetBuffer("_cacheRadianceOut", _cacheRadiance[0]);
        _propagateMaterial.SetBuffer("_cacheGeometryOut", _cacheGeometry[0]);

        _traceMaterial.SetBuffer("_data", _dataBuffer);
        _traceMaterial.SetBuffer("_cacheRadiance", _cacheRadiance[0]);
        _resolveMaterial.SetBuffer("_data", _dataBuffer);
    }

    private void BindResolutionResources()
    {
        _traceMaterial.SetRenderTexture("_diffuseRaw", _diffuseRaw);
        _traceMaterial.SetRenderTexture("_specularRaw", _specularRaw);
        _resolveMaterial.SetRenderTexture("_diffuseRaw", _diffuseRaw);
        _resolveMaterial.SetRenderTexture("_specularRaw", _specularRaw);
    }

    private static void ValidateTraceResolutionScale(float scale)
    {
        if (!float.IsFinite(scale) || scale < 0.25f || scale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Trace resolution scale must be in [0.25, 1].");
        }
    }

    private uint TraceWidth(uint width) => Math.Max((uint)MathF.Ceiling(width * _traceResolutionScale), 1u);
    private uint TraceHeight(uint height) => Math.Max((uint)MathF.Ceiling(height * _traceResolutionScale), 1u);

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        _gbufferWidth = Math.Max(width, 1u);
        _gbufferHeight = Math.Max(height, 1u);
        _diffuseRaw.Resize(TraceWidth(_gbufferWidth), TraceHeight(_gbufferHeight));
        _specularRaw.Resize(TraceWidth(_gbufferWidth), TraceHeight(_gbufferHeight));
        for (int i = 0; i < 2; i++)
        {
            _diffuseHistory[i].Resize(_gbufferWidth, _gbufferHeight);
            _specularHistory[i].Resize(_gbufferWidth, _gbufferHeight);
        }
        _boundGBuffer = null;
        _historyValid = false;
        UpdateMemoryEstimate();
    }

    /// <inheritdoc />
    public void Execute(RenderPluginContext context)
    {
        long recordStart = Stopwatch.GetTimestamp();
        if (!Matrix4x4.Invert(context.InvViewProjection, out Matrix4x4 viewProjection))
        {
            viewProjection = Matrix4x4.Identity;
        }

        Vector3 cameraPosition = context.CameraTransform.Position;
        UpdateCacheOrigins(cameraPosition);
        if (_frameIndex == 0)
        {
            Array.Copy(_cacheOrigins, _previousCacheOrigins, CascadeCount);
            _previousCameraPosition = cameraPosition;
            _previousViewProjection = viewProjection;
        }
        else if (Vector3.DistanceSquared(cameraPosition, _previousCameraPosition)
            > MathF.Pow(_baseCellSize * _gridResolution * 0.5f, 2.0f))
        {
            _historyValid = false;
        }

        PBRDeferredPipeline.DeferredLightingData lighting = context.LightingData;
        RadianceCacheData data = new()
        {
            InvViewProjection = context.InvViewProjection,
            ViewProjection = viewProjection,
            ViewProjectionPrev = _previousViewProjection,
            SunViewProjection0 = lighting.SunViewProjection0,
            SunViewProjection1 = lighting.SunViewProjection1,
            SunViewProjection2 = lighting.SunViewProjection2,
            SunViewProjection3 = lighting.SunViewProjection3,
            CameraPosition = new Vector4(cameraPosition, 0.0f),
            PreviousCameraPosition = new Vector4(_previousCameraPosition, 0.0f),
            SunDirection = lighting.SunDirection,
            SunColorAndIntensity = lighting.SunColorAndIntensity,
            SkyHorizonColor = lighting.SkyHorizonColor,
            SkyZenithColor = lighting.SkyZenithColor,
            CascadeSplits = lighting.CascadeSplits,
            CascadeTexelSizes = lighting.CascadeTexelSizes,
            CacheOrigin0 = _cacheOrigins[0],
            CacheOrigin1 = _cacheOrigins[1],
            CacheOrigin2 = _cacheOrigins[2],
            PreviousCacheOrigin0 = _previousCacheOrigins[0],
            PreviousCacheOrigin1 = _previousCacheOrigins[1],
            PreviousCacheOrigin2 = _previousCacheOrigins[2],
            CacheParams = new Vector4(_gridResolution, CascadeCount, _cacheCellCount, _frameIndex),
            ViewportParams = new Vector4(context.Width, context.Height, _diffuseRaw.Width, _diffuseRaw.Height),
            LightingParams = new Vector4(lighting.Params.X, lighting.Params.Y, lighting.Params.Z, EmissiveScale),
            ResponseParams = new Vector4(CacheHysteresis, TemporalHysteresis, BounceStrength, SkyIntensity),
            TraceParams = new Vector4(TraceMaxDistance, _historyValid ? 1.0f : 0.0f, PropagationStrength, OffscreenRetention),
        };
        _dataBuffer.UpdateBuffer(data);
        BindFrameResources(context);

        _commandBuffer.Begin();
        using (GPUCommandBuffer.ComputePass cachePass = _commandBuffer.BeginCompute())
        {
            _clearMaterial.DispatchBySize(cachePass, (uint)_cacheCellCount, 1, 1);
            _injectMaterial.DispatchBySize(cachePass, (context.Width + 1) / 2, (context.Height + 1) / 2, 1);
            _updateMaterial.DispatchBySize(cachePass, (uint)_cacheCellCount, 1, 1);
            _propagateMaterial.DispatchBySize(cachePass, (uint)_cacheCellCount, 1, 1);
            _traceMaterial.DispatchBySize(cachePass, _diffuseRaw.Width, _diffuseRaw.Height, 1);
        }

        int historyWriteIndex = 1 - _historyReadIndex;
        _resolveMaterial.SetRenderTexture("_diffuseHistory", _diffuseHistory[_historyReadIndex]);
        _resolveMaterial.SetRenderTexture("_specularHistory", _specularHistory[_historyReadIndex]);
        _resolveMaterial.SetRenderTexture("_diffuseOut", _diffuseHistory[historyWriteIndex]);
        _resolveMaterial.SetRenderTexture("_specularOut", _specularHistory[historyWriteIndex]);
        using (GPUCommandBuffer.ComputePass resolvePass = _commandBuffer.BeginCompute())
        {
            _resolveMaterial.DispatchBySize(resolvePass, context.Width, context.Height, 1);
        }
        _commandBuffer.End();
        _device.Submit(_commandBuffer);

        _historyReadIndex = historyWriteIndex;
        _historyValid = true;
        _previousViewProjection = viewProjection;
        _previousCameraPosition = cameraPosition;
        Array.Copy(_cacheOrigins, _previousCacheOrigins, CascadeCount);
        _frameIndex++;

        context.GIDiffuse = _diffuseHistory[_historyReadIndex];
        context.GISpecular = _specularHistory[_historyReadIndex];

        double recordMilliseconds = (double)(Stopwatch.GetTimestamp() - recordStart)
            / Stopwatch.Frequency * 1000.0;
        Statistics = new RadianceCacheStatistics(_cacheCellCount, _memoryBytes, recordMilliseconds);
        if (!_profileCounterRegistered)
        {
            _profileCounter = context.Profiler.RegisterCounter("RadianceCacheGI", "Total");
            _profileCounterRegistered = true;
        }
        context.Profiler.PushValue(_profileCounter, recordMilliseconds);
    }

    private void UpdateCacheOrigins(Vector3 cameraPosition)
    {
        for (int cascade = 0; cascade < CascadeCount; cascade++)
        {
            float cellSize = _baseCellSize * (1 << cascade);
            Vector3 snappedCenter = new(
                MathF.Floor(cameraPosition.X / cellSize) * cellSize,
                MathF.Floor(cameraPosition.Y / cellSize) * cellSize,
                MathF.Floor(cameraPosition.Z / cellSize) * cellSize);
            Vector3 origin = snappedCenter - new Vector3(_gridResolution * cellSize * 0.5f);
            _cacheOrigins[cascade] = new Vector4(origin, cellSize);
        }
    }

    private void BindFrameResources(RenderPluginContext context)
    {
        if (!ReferenceEquals(_boundGBuffer, context.GBuffer))
        {
            _injectMaterial.SetRenderTextureDepth("_gbufferDepth", context.GBuffer);
            _injectMaterial.SetRenderTexture("_albedo", context.GBuffer, 0);
            _injectMaterial.SetRenderTexture("_normal", context.GBuffer, 1);
            _injectMaterial.SetRenderTexture("_emissive", context.GBuffer, 3);
            _traceMaterial.SetRenderTextureDepth("_gbufferDepth", context.GBuffer);
            _traceMaterial.SetRenderTexture("_albedo", context.GBuffer, 0);
            _traceMaterial.SetRenderTexture("_normal", context.GBuffer, 1);
            _traceMaterial.SetRenderTexture("_mrAO", context.GBuffer, 2);
            _traceMaterial.SetRenderTexture("_emissive", context.GBuffer, 3);
            _resolveMaterial.SetRenderTextureDepth("_gbufferDepth", context.GBuffer);
            _resolveMaterial.SetRenderTexture("_normal", context.GBuffer, 1);
            _boundGBuffer = context.GBuffer;
        }
        if (!ReferenceEquals(_boundShadowMap, context.ShadowMap))
        {
            _injectMaterial.SetRenderTextureDepth("_shadowMap", context.ShadowMap);
            _boundShadowMap = context.ShadowMap;
        }
        if (context.PointLightBuffer != null && !ReferenceEquals(_boundPointLights, context.PointLightBuffer))
        {
            _injectMaterial.SetBuffer("_pointLights", context.PointLightBuffer);
            _boundPointLights = context.PointLightBuffer;
        }
    }

    private void UpdateMemoryEstimate()
    {
        long cacheBufferBytes = (long)_cacheCellCount * 16 * 6;
        long rawTextureBytes = (long)_diffuseRaw.Width * _diffuseRaw.Height * 8 * 2;
        long historyTextureBytes = (long)_gbufferWidth * _gbufferHeight * 8 * 4;
        _memoryBytes = cacheBufferBytes + rawTextureBytes + historyTextureBytes;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _diffuseRaw.Dispose();
            _specularRaw.Dispose();
            for (int i = 0; i < 2; i++)
            {
                _diffuseHistory[i].Dispose();
                _specularHistory[i].Dispose();
                _cacheRadiance[i].Dispose();
                _cacheGeometry[i].Dispose();
            }
            _accumRadiance.Dispose();
            _accumNormal.Dispose();
            _dataBuffer.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
