using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Per-frame diagnostics and fixed memory budgets for voxel GI.</summary>
public readonly struct VoxelGiStatistics
{
    /// <summary>Gets the number of resident structural bricks.</summary>
    public int StaticResidentBricks { get; }
    /// <summary>Gets the structural physical-page capacity.</summary>
    public int StaticCapacityBricks { get; }
    /// <summary>Gets the number of resident movable-geometry bricks.</summary>
    public int DynamicResidentBricks { get; }
    /// <summary>Gets the movable physical-page capacity.</summary>
    public int DynamicCapacityBricks { get; }
    /// <summary>Gets the structural bricks still queued after this frame.</summary>
    public int PendingStaticBricks { get; }
    /// <summary>Gets the structural bricks processed this frame.</summary>
    public int StaticBricksUpdated { get; }
    /// <summary>Gets the movable bricks processed this frame.</summary>
    public int DynamicBricksUpdated { get; }
    /// <summary>Gets the brick allocations dropped because a physical pool was full.</summary>
    public int DroppedBricks { get; }
    /// <summary>Gets the number of active persistent instances.</summary>
    public int StaticInstanceCount { get; }
    /// <summary>Gets the number of unique GPU-resident mesh geometries.</summary>
    public int SharedMeshCount { get; }
    /// <summary>Gets the fixed sparse attribute-pool allocation in bytes.</summary>
    public long AttributeMemoryBytes { get; }
    /// <summary>Gets the dense mipmapped radiance allocation in bytes.</summary>
    public long RadianceMemoryBytes { get; }
    /// <summary>Gets CPU time spent preparing, encoding and submitting the GI work.</summary>
    public double CpuRecordMilliseconds { get; }
    /// <summary>Gets the averaged GPU duration of volume-update frames, or NaN before the first sample.</summary>
    public double GpuMilliseconds { get; }
    /// <summary>
    /// Gets the total number of resident bricks dispatched this frame across all
    /// clipmap levels (inject + propagate). Compare to <see cref="DenseBrickTotal"/>
    /// to see the sparse dispatch reduction ratio.
    /// </summary>
    public int SparseBrickTotal { get; }
    /// <summary>
    /// Gets the total number of bricks that would have been dispatched with dense
    /// dispatch (<c>bricksPerLevel × LevelCount</c>). Used as the denominator for
    /// the sparse dispatch reduction ratio.
    /// </summary>
    public int DenseBrickTotal { get; }

    /// <summary>Creates one immutable diagnostic snapshot.</summary>
    /// <param name="staticResidentBricks">The resident structural-brick count.</param>
    /// <param name="staticCapacityBricks">The structural pool capacity.</param>
    /// <param name="dynamicResidentBricks">The resident movable-brick count.</param>
    /// <param name="dynamicCapacityBricks">The movable pool capacity.</param>
    /// <param name="pendingStaticBricks">The queued structural-brick count.</param>
    /// <param name="staticBricksUpdated">The structural bricks processed this frame.</param>
    /// <param name="dynamicBricksUpdated">The movable bricks processed this frame.</param>
    /// <param name="droppedBricks">The failed page allocations this frame.</param>
    /// <param name="staticInstanceCount">The active structural-instance count.</param>
    /// <param name="sharedMeshCount">The unique shared geometry count.</param>
    /// <param name="attributeMemoryBytes">The sparse attribute allocation.</param>
    /// <param name="radianceMemoryBytes">The radiance allocation.</param>
    /// <param name="cpuRecordMilliseconds">The CPU encode duration.</param>
    /// <param name="gpuMilliseconds">The last GPU timestamp duration.</param>
    /// <param name="sparseBrickTotal">The resident brick count dispatched this frame.</param>
    /// <param name="denseBrickTotal">The dense-equivalent brick count.</param>
    public VoxelGiStatistics(
        int staticResidentBricks,
        int staticCapacityBricks,
        int dynamicResidentBricks,
        int dynamicCapacityBricks,
        int pendingStaticBricks,
        int staticBricksUpdated,
        int dynamicBricksUpdated,
        int droppedBricks,
        int staticInstanceCount,
        int sharedMeshCount,
        long attributeMemoryBytes,
        long radianceMemoryBytes,
        double cpuRecordMilliseconds,
        double gpuMilliseconds,
        int sparseBrickTotal,
        int denseBrickTotal)
    {
        StaticResidentBricks = staticResidentBricks;
        StaticCapacityBricks = staticCapacityBricks;
        DynamicResidentBricks = dynamicResidentBricks;
        DynamicCapacityBricks = dynamicCapacityBricks;
        PendingStaticBricks = pendingStaticBricks;
        StaticBricksUpdated = staticBricksUpdated;
        DynamicBricksUpdated = dynamicBricksUpdated;
        DroppedBricks = droppedBricks;
        StaticInstanceCount = staticInstanceCount;
        SharedMeshCount = sharedMeshCount;
        AttributeMemoryBytes = attributeMemoryBytes;
        RadianceMemoryBytes = radianceMemoryBytes;
        CpuRecordMilliseconds = cpuRecordMilliseconds;
        GpuMilliseconds = gpuMilliseconds;
        SparseBrickTotal = sparseBrickTotal;
        DenseBrickTotal = denseBrickTotal;
    }
}

/// <summary>
/// Debug visualization modes for voxel GI output.
/// </summary>
public enum VoxelGiDebugMode
{
    /// <summary>Normal rendering — no debug overlay.</summary>
    Off = 0,
    /// <summary>Show the diffuse irradiance contribution only.</summary>
    DiffuseIrradiance = 1,
    /// <summary>Show the indirect specular contribution only.</summary>
    IndirectSpecular = 2,
    /// <summary>Show the GI visibility (occlusion) term.</summary>
    Visibility = 3,
    /// <summary>Show the raw diffuse trace before temporal accumulation.</summary>
    RawDiffuseTrace = 4,
    /// <summary>Show SSR hit confidence independently of reflected radiance.</summary>
    SsrConfidence = 5,
}

/// <summary>
/// The complete set of shaders required by <see cref="RGNode_VoxelGI"/>.
/// Load each from its HLSL file and pass to the constructor.
/// </summary>
public readonly struct VoxelGiShaders
{
    /// <summary>The voxel clear shader (VoxelClear.hlsl).</summary>
    public required Shader Clear { get; init; }
    /// <summary>The triangle voxelization shader (Voxelize.hlsl).</summary>
    public required Shader Voxelize { get; init; }
    /// <summary>The direct light injection shader (VoxelInject.hlsl).</summary>
    public required Shader Inject { get; init; }
    /// <summary>The radiance mip downsample shader (VoxelMip.hlsl).</summary>
    public required Shader Mip { get; init; }
    /// <summary>The cascading mip chain shader (VoxelMipChain.hlsl).</summary>
    public required Shader MipChain { get; init; }
    /// <summary>The multi-bounce propagation shader (VoxelPropagate.hlsl).</summary>
    public required Shader Propagate { get; init; }
    /// <summary>The cone tracing shader (VoxelTrace.hlsl).</summary>
    public required Shader Trace { get; init; }
    /// <summary>The temporal demosaic shader (VoxelDemosaic.hlsl).</summary>
    public required Shader Demosaic { get; init; }
    /// <summary>
    /// The blue-noise tile bake shader (ScreenSpaceReflectionBlueNoise.hlsl),
    /// shared with the SSR trace. The baked tile jitters the cone march.
    /// </summary>
    public required Shader BlueNoise { get; init; }
    /// <summary>The full-resolution upsample shader (VoxelGiUpsample.hlsl), or null when not used as a plugin.</summary>
    public Shader? Upsample { get; init; }
}

/// <summary>
/// Voxel global illumination renderer for the deferred PBR pipeline: a cascaded
/// voxel clipmap (4 levels, each a cube of <c>resolution</c>^3 voxels at twice
/// the previous level's voxel size, following the camera) with compute
/// voxelization, direct-light injection, deterministic rotation-balanced
/// diffuse cone tracing and the off-screen voxel-cone reflection fallback used
/// by the post-lighting screen-space reflection renderer.
/// <br/>Mesh geometry is registered once through <see cref="RegisterMesh"/> and
/// shared by persistent structural instances and per-frame movable instances.
/// Structural bricks are rebuilt incrementally after edits or camera scrolling.
/// <br/>Call <see cref="Render"/> after the G-buffer pass and before the lighting
/// pass; the resulting configurable-resolution <see cref="IndirectTexture"/>
/// atlas is upsampled internally to the full-resolution <see cref="DiffuseTexture"/>
/// and <see cref="SpecularTexture"/> outputs that the deferred lighting pass samples.
/// <br/>Attribute voxels live in storage buffers (packed, point-sampled by the
/// injection pass); the HDR radiance volume is one mip-mapped RGBA16Float
/// <see cref="Texture3D"/> with all clipmap levels stacked along its depth axis,
/// cone-traced with hardware trilinear filtering.
/// </summary>
public sealed class RGNode_VoxelGI : AutoDisposable, IRenderGraphNode
{
    /// <summary>
    /// Per-frame data uploaded to every voxel GI shader. Layout must match the
    /// <c>_data</c> cbuffer in VoxelCommon.hlsli exactly. Assembled internally by
    /// the renderer from pipeline data and user-tunable properties.
    /// </summary>
    private struct VoxelGiData
    {
        /// <summary>Inverse of the camera view-projection matrix.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>Previous frame view-projection for temporal reprojection (filled by the renderer).</summary>
        public Matrix4x4 ViewProjectionPrev;
        /// <summary>Camera view-projection matrix (filled by the renderer).</summary>
        public Matrix4x4 ViewProjection;
        /// <summary>Sun light view-projection matrix of shadow cascade 0 (nearest).</summary>
        public Matrix4x4 SunViewProjection0;
        /// <summary>Sun light view-projection matrix of shadow cascade 1.</summary>
        public Matrix4x4 SunViewProjection1;
        /// <summary>Sun light view-projection matrix of shadow cascade 2.</summary>
        public Matrix4x4 SunViewProjection2;
        /// <summary>Sun light view-projection matrix of shadow cascade 3 (farthest).</summary>
        public Matrix4x4 SunViewProjection3;
        /// <summary>Clipmap level 0 origin: xyz = min corner in world space, w = voxel size (filled by the renderer).</summary>
        public Vector4 LevelOrigin0;
        /// <summary>Clipmap level 1 origin (filled by the renderer).</summary>
        public Vector4 LevelOrigin1;
        /// <summary>Clipmap level 2 origin (filled by the renderer).</summary>
        public Vector4 LevelOrigin2;
        /// <summary>Clipmap level 3 origin (filled by the renderer).</summary>
        public Vector4 LevelOrigin3;
        /// <summary>Clipmap level 0 toroidal storage offset in voxels (filled by the renderer).</summary>
        public Vector4 LevelRingOffset0;
        /// <summary>Clipmap level 1 toroidal storage offset in voxels (filled by the renderer).</summary>
        public Vector4 LevelRingOffset1;
        /// <summary>Clipmap level 2 toroidal storage offset in voxels (filled by the renderer).</summary>
        public Vector4 LevelRingOffset2;
        /// <summary>Clipmap level 3 toroidal storage offset in voxels (filled by the renderer).</summary>
        public Vector4 LevelRingOffset3;
        /// <summary>Camera position in world space (w unused).</summary>
        public Vector4 CameraPosition;
        /// <summary>Normalized direction the sun light travels (w unused).</summary>
        public Vector4 SunDirection;
        /// <summary>Sun linear color (rgb) and intensity (w).</summary>
        public Vector4 SunColorAndIntensity;
        /// <summary>Azimuthally filtered physical-sky radiance at the horizon.</summary>
        public Vector4 SkyHorizonColor;
        /// <summary>Physical-sky radiance at the zenith.</summary>
        public Vector4 SkyZenithColor;
        /// <summary>View-distance end boundary of each shadow cascade.</summary>
        public Vector4 CascadeSplits;
        /// <summary>World units per shadow texel of each cascade.</summary>
        public Vector4 CascadeTexelSizes;
        /// <summary>x=level resolution y=level count z=mip count w=voxel specular enabled (filled by the renderer).</summary>
        public Vector4 ClipmapParams;
        /// <summary>x=shadowEnabled y=numPointLights z=shadowMapSize w=unused.</summary>
        public Vector4 LightingParams;
        /// <summary>x=emissiveScale y=traceMaxDistance zw=trace resolution in pixels (filled by the renderer).</summary>
        public Vector4 GiParams;
        /// <summary>x=debugView yz=G-buffer resolution in pixels (filled by the renderer) w=giSkyIntensity (sky light multiplier for voxel GI).</summary>
        public Vector4 GiParams2;
        /// <summary>x=frame index, y=GI diffuse bias, z=history-valid flag (filled by the renderer), w=diffuse spreading (dual-kernel opacity bias).</summary>
        public Vector4 GiFrameParams;
        /// <summary>RSM sun bounce: x=intensity (0 disables), y=max injection distance in world units, z=NDC depth tolerance scale, w=minimum bounce albedo.</summary>
        public Vector4 RsmParams;
        /// <summary>RSM sun bounce: xy=RSM resolution in texels (zw unused).</summary>
        public Vector4 RsmParams2;
    }

    /// <summary>
    /// Per-frame data uploaded to the VoxelGiUpsample compute pass. Layout must
    /// match the <c>_data</c> cbuffer in VoxelGiUpsample.hlsl exactly.
    /// </summary>
    public struct VoxelGiUpsampleData
    {
        /// <summary>Inverse of the camera view-projection matrix for linear-depth reconstruction.</summary>
        public Matrix4x4 InvViewProjection;
        /// <summary>xy = G-buffer size in pixels, z = 1/traceWidth (= 5/atlasWidth), w = 1/traceHeight.</summary>
        public Vector4 Params;
    }

    /// <summary>
    /// Push constant payload for one voxelize dispatch. Layout must match the
    /// <c>VoxelizeConstants</c> struct in Voxelize.hlsl exactly (128 bytes, the
    /// device push-constant limit — the dirty-brick range is bit-packed into
    /// Params2 to keep the payload at that size).
    /// </summary>
    private struct VoxelizeConstants
    {
        public Matrix4x4 Model;
        public Vector4 BaseColor;
        public Vector4 Emissive;
        public Vector4 Params;  // x=levelIndex, y=indexIs16Bit, z=vertexStrideUints, w=alphaCutoff
        public Vector4 Params2; // x=triangleCount, y/z=packed voxel-space dirty range lo/hi (x|y<<8|z<<16), w=unused
    }

    /// <summary>GPU-resident geometry shared by all GI material registrations of one mesh.</summary>
    private sealed class MeshGeometry
    {
        public required GraphicsBuffer Vertices;
        public required GraphicsBuffer Indices;
        public required VoxelGiBounds LocalBounds;
        public uint TriangleCount;
        public uint VertexStrideUints;
        public bool Index16Bit;
    }

    /// <summary>A registered GI material view of shared mesh geometry.</summary>
    private sealed class MeshRegistration
    {
        public required MeshGeometry Geometry;
        public Texture2D? Albedo;
        public Texture2D? Emissive;
    }

    /// <summary>A persistent structural instance stored in the static clipmap.</summary>
    private sealed class StaticInstance
    {
        public required MeshRegistration Registration;
        public Matrix4x4 World;
        public Vector4 BaseColor;
        public Vector3 Emissive;
        public float AlphaCutoff;
        public VoxelGiBounds WorldBounds;
        public bool Active;
    }

    /// <summary>A dynamic mesh instance submitted for one frame.</summary>
    private struct DynamicInstance
    {
        public MeshRegistration Registration;
        public Matrix4x4 World;
        public Vector4 BaseColor;
        public Vector3 Emissive;
        public float AlphaCutoff;
        public VoxelGiBounds WorldBounds;
    }

    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    // One-off geometry upload buffer used by CreateGeometry; per-frame compute
    // dispatches record into the graph's shared command buffer instead.
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _clearMaterial;
    private readonly ComputeMaterial _voxelizeMaterial;
    private readonly ComputeMaterial _injectMaterial;
    private readonly ComputeMaterial _mipMaterial;
    private readonly ComputeMaterial _mipChainMaterial;
    private readonly ComputeMaterial _propagateMaterial;
    private readonly ComputeMaterial _traceMaterial;
    private readonly ComputeMaterial _demosaicMaterial;
    // The blue-noise tile is baked once with a graphics pass, then sampled by
    // the compute trace. Must match BLUE_NOISE_TILE in VoxelTrace.hlsl and
    // SSR_BLUE_NOISE_SIZE in the bake shader.
    private const uint BlueNoiseTextureSize = 128;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _blueNoiseMaterial;
    private readonly GPUAttachmentLayout _blueNoiseLayout;
    private readonly RenderTexture _blueNoiseTexture;
    private bool _blueNoiseBaked;
    private ComputeMaterial? _upsampleMaterial;
    private GraphicsValueBuffer<VoxelGiUpsampleData>? _upsampleDataBuffer;
    private readonly GraphicsValueBuffer<VoxelGiData> _dataBuffer;
    private readonly GpuTimestampSampler? _gpuTimestamps;

    /// <summary>
    /// The number of timestamp slots reserved for per-stage GPU timing.
    /// Slot 0 = main pass begin, slots 1–6 = in-pass stage boundaries,
    /// slot 7 = main pass end, slots 8–9 = upsample pass begin/end.
    /// </summary>
    private const int TimestampSlotCount = 10;

    /// <summary>
    /// Per-stage GPU durations in milliseconds, indexed by stage enum. These are
    /// exponential moving averages over sampled volume-update frames only: on
    /// skipped frames the update stages are bracketed by back-to-back
    /// timestamps (~0 ms), so folding those in would alias the counters between
    /// ~0 and the true cost at the beat of the ~1 Hz sampler against the
    /// volume refresh rate.
    /// </summary>
    private readonly double[] _stageGpuMilliseconds = new double[GiStageCount];

    private readonly int _resolution;
    private readonly int _mipCount;
    private readonly float _baseVoxelSize;
    private readonly long _attributeMemoryBytes;
    private readonly long _radianceMemoryBytes;
    private readonly VoxelGiClipmap _clipmap;

    // Static and dynamic attributes use compact physical brick pools. Small
    // toroidal page tables map each level's logical coordinates into the pools.
    private readonly GraphicsBuffer _attrStatic;
    private readonly GraphicsBuffer _attrDynamic;
    private readonly GraphicsBuffer[] _pageTableStatic = new GraphicsBuffer[LevelCount];
    private readonly GraphicsBuffer[] _pageTableDynamic = new GraphicsBuffer[LevelCount];
    private readonly GraphicsBuffer[] _dirtyBrickCoordinates = new GraphicsBuffer[LevelCount];
    private readonly GraphicsBuffer[] _residentBrickCoordinates = new GraphicsBuffer[LevelCount];
    // Combined page table for inject/propagate: interleaved (static, dynamic)
    // uint pairs so both pools share one descriptor set.
    private readonly GraphicsBuffer[] _pageTableCombined = new GraphicsBuffer[LevelCount];
    private readonly uint[][] _combinedPageTableScratch = new uint[LevelCount][];
    private readonly VoxelGiPagePool _staticPagePool;
    private readonly VoxelGiPagePool _dynamicPagePool;
    // Double-buffered radiance: propagate writes bounce results directly into
    // the alternate texture's mip 0, avoiding a separate copy-back pass. The
    // opacity volume is single-buffered (propagate does not modify it).
    private readonly Texture3D[] _radiance = new Texture3D[2];
    private readonly Texture3D _opacity;
    private uint _frameIndex;
    private double _gpuMilliseconds = double.NaN;
    /// <summary>False until the first sampled update frame seeds the GPU averages directly (avoids a zero-dragged warm-up).</summary>
    private bool _gpuAveragesPrimed;
    /// <summary>Whether the sample frame whose timestamps are pending readback ran the volume update.</summary>
    private bool _sampledVolumeUpdate;

    /// <summary>The number of measured GPU stages (matches profiler counters).</summary>
    private const int GiStageCount = 7;

    // Profiler counter handles — lazily registered on first Execute call.
    private RenderProfileCounterId _giTotalCounter;
    private RenderProfileCounterId _giGpuCounter;
    private readonly RenderProfileCounterId[] _giStageCounters = new RenderProfileCounterId[GiStageCount];
    private bool _profilerCountersRegistered;

    private readonly Dictionary<(Mesh Mesh, uint VertexStrideBytes), MeshGeometry> _geometryByMesh = new();
    private readonly List<MeshGeometry> _geometries = new();
    private readonly List<MeshRegistration> _meshes = new();
    private readonly List<StaticInstance?> _staticInstances = new();
    private readonly Stack<int> _freeStaticInstanceHandles = new();
    private readonly BvhAabb3D _staticBvh = new();
    private readonly List<BoundingBox3D> _staticBvhBounds = new();
    private readonly List<StaticInstance> _staticBvhInstances = new();
    private readonly List<int> _staticBvhResults = new();
    private bool _staticBvhDirty = true;
    private readonly BvhAabb3D _dynamicBvh = new();
    private readonly List<BoundingBox3D> _dynamicBvhBounds = new();
    private readonly List<int> _dynamicBvhResults = new();
    private readonly List<DynamicInstance> _instances = new();
    private readonly List<VoxelGiDirtyBrick> _dirtyBricks = new();
    private readonly List<VoxelGiDirtyBrick> _candidateBricks = new();
    private readonly HashSet<uint> _brickKeys = new();
    private readonly List<VoxelGiDirtyBrick> _residentBricks = new();
    private readonly List<VoxelGiDirtyBrick> _staleBricks = new();
    private readonly bool[][] _currentResidentLogical = new bool[LevelCount][];
    private readonly bool[][] _previousResidentLogical = new bool[LevelCount][];
    private readonly int[] _residentCounts = new int[LevelCount];
    private readonly int[] _staleCounts = new int[LevelCount];
    private readonly bool[] _staticNeedsFullClear = new bool[LevelCount];

    // ── Rate-limited volume update ──
    // The movable-geometry rebuild, inject, mip-chain and propagate stages
    // run at most VolumeRefreshRate times per second. On skipped frames
    // the trace stage samples the persistent final radiance texture; the
    // diffuse temporal resolve (~5-frame window) smooths the quantized
    // updates, so the only visible effect is indirect light from moving
    // objects and lights lagging by up to the refresh interval.
    private bool _volumeInitialized;
    private float _volumeUpdateElapsedSeconds;
    private float _volumeRefreshRate = 30.0f;

    private RenderTexture _traceRaw;
    // Previous frame's temporally accumulated raw diffuse/ALD trace. The two
    // textures swap roles after submission, so VoxelTrace can read history and
    // write the new accumulation without a separate resolve pass.
    private RenderTexture _traceHistory;
    private RenderTexture _indirectAtlas;
    // Facades of the graph-owned transients below; null until Attach creates them.
    private RenderTexture? _giDiffuseFullRes;
    private RenderTexture? _giSpecularFullRes;
    private readonly RenderTexture[] _historyGI = new RenderTexture[2];
    private uint _gbufferWidth;
    private uint _gbufferHeight;
    private float _traceResolutionScale;
    private int _historyReadIndex;
    private bool _historyValid;
    private bool _ssrOnly;
    private Matrix4x4 _viewProjectionPrev = Matrix4x4.Identity;
    private RenderTexture? _boundGBuffer;
    private RenderTexture? _boundShadowMap;
    private GraphicsBuffer? _boundPointLightBuffer;

    // RSM sun bounce (reflective shadow map of the selected CSM cascade).
    // _rsmMapResource is the graph transient written by the app's RGNode_RsmPass;
    // null runs the trace with a 1x1 far-depth fallback so the shader bindings
    // stay complete. _rsmBound guards the first bind (the ctor binds the
    // fallback before any real map exists).
    private RenderGraphTexture? _rsmMapResource;
    private RenderTexture? _boundRsmMap;
    private RenderTexture? _rsmFallbackDepth;
    private bool _rsmBound;
    private int _rsmCascadeIndex = 2;

    // Graph-owned transient resources. _giDiffuseFullRes and _giSpecularFullRes are
    // facades of the transients below (not disposed here, rematerialized on resize).
    private RenderGraph? _graph;
    private RGNode_DeferredLighting? _lighting;
    private RenderGraphTexture? _gbufferResource;
    private RenderGraphTexture? _shadowMapResource;
    private PBRSceneEnvironment? _environment;
    private RenderGraphTexture? _giDiffuseResource;
    private RenderGraphTexture? _giSpecularResource;

    private const int LevelCount = 4;
    private const int BrickSize = 8;

    private readonly int[] _staticBrickBudgets = [256, 128, 64, 32];

    /// <summary>
    /// Gets the maximum number of structural bricks rebuilt per frame for one
    /// clipmap level. High-priority edit bricks are processed before
    /// camera-streaming bricks. The budget caps the voxelization work of any
    /// single frame when the camera crosses a brick boundary (all levels get
    /// dirty simultaneously); the trade-off of lower values is that
    /// newly-exposed geometry takes more frames to fully voxelize. Coarser
    /// levels cover much more world space per brick, so giving them a smaller
    /// budget than the fine levels smooths frame spikes better than a uniform
    /// value.
    /// </summary>
    public int GetStaticBrickBudget(int level)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, LevelCount);
        return _staticBrickBudgets[level];
    }

    /// <summary>
    /// Sets the per-frame structural brick budget for one clipmap level. Zero
    /// pauses structural voxelization for that level (its queue keeps growing);
    /// negative values are clamped to zero.
    /// </summary>
    public void SetStaticBrickBudget(int level, int budget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, LevelCount);
        _staticBrickBudgets[level] = Math.Max(budget, 0);
    }

    /// <summary>
    /// Gets or sets how many nearest clipmap levels receive per-frame movable geometry.
    /// Structural geometry remains available in every level.
    /// </summary>
    public int DynamicLevelCount { get; set; } = 2;

    /// <summary>
    /// Gets or sets the number of indirect light bounces propagated through the
    /// radiance volume each frame. Zero disables bounce (direct lighting only).
    /// </summary>
    public int BounceCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the bounce light strength multiplier. Higher values produce
    /// brighter indirect bounces.
    /// </summary>
    public float BounceStrength { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the temporal hysteresis (0..1). Higher values retain more
    /// history, producing smoother but more laggy indirect lighting.
    /// </summary>
    public float TemporalHysteresis { get; set; } = 0.8f;

    /// <summary>
    /// Gets or sets the post-demosaic diffuse temporal hysteresis (0..1),
    /// independently from specular. The raw-trace accumulation converges for
    /// static scenes but collapses to single-cone noise wherever its own
    /// reprojection rejects history (camera motion across depth edges), so the
    /// demosaic stage keeps a second, neighbourhood-clamped accumulation. The
    /// effective hysteresis halves under camera motion (see VoxelDemosaic.hlsl)
    /// to stay responsive to the scrolling voxel field; zero disables it.
    /// </summary>
    public float DiffuseTemporalHysteresis { get; set; } = 0.85f;

    /// <summary>
    /// Gets or sets the diffuse spreading amount for the dual-kernel opacity
    /// bias. Lowers the elevation of each diffuse cone toward the surface
    /// tangent, gathering more near-field occlusion for stronger contact AO.
    /// Zero disables the effect (radiance kernel only).
    /// </summary>
    public float DiffuseSpreading { get; set; } = 0.5f;

    /// <summary>
    /// Gets or sets the screen-space cone-trace resolution relative to the
    /// G-buffer (0.25..1.0). Changing it recreates only the trace, resolve and
    /// temporal-history textures; the voxel clipmap remains intact. The caller
    /// must rebind <see cref="IndirectTexture"/> to its lighting pipeline after
    /// changing this value.
    /// </summary>
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

            float previousScale = _traceResolutionScale;
            _traceResolutionScale = value;
            try
            {
                Resize(_gbufferWidth, _gbufferHeight);
            }
            catch
            {
                _traceResolutionScale = previousScale;
                throw;
            }
        }
    }

    /// <summary>
    /// Gets or sets the emissive scale multiplier for direct-light injection.
    /// Boosts emissive surface contribution to the voxel volume. Zero disables
    /// emissive injection.
    /// </summary>
    public float EmissiveScale { get; set; }

    /// <summary>
    /// Gets or sets the maximum world-space cone-trace distance. Beyond this
    /// distance, cones return no radiance, limiting artifacts from far geometry.
    /// </summary>
    public float TraceMaxDistance { get; set; } = 20.0f;

    /// <summary>
    /// Gets or sets whether the post-lighting reflection path runs without its
    /// voxel specular-cone fallback. Diffuse voxel GI remains active.
    /// </summary>
    public bool SsrOnly
    {
        get => _ssrOnly;
        set
        {
            if (value == _ssrOnly)
            {
                return;
            }

            _ssrOnly = value;
            // Do not let the demosaic history retain the previously selected
            // reflection source during an SSR/voxel A/B comparison.
            _historyValid = false;
        }
    }

    /// <summary>
    /// Gets or sets the sky-light multiplier for voxel GI. Scales the sky
    /// radiance injected into the voxel volume.
    /// </summary>
    public float SkyIntensity { get; set; } = 3.0f;

    /// <summary>
    /// Gets or sets the debug visualization mode.
    /// </summary>
    public VoxelGiDebugMode DebugView { get; set; }

    /// <summary>
    /// Gets or sets the maximum refresh rate of the voxel radiance volume
    /// (movable-geometry rebuild + inject + mip chain + propagate) in updates
    /// per second. 0 updates every frame; values above 0 cap the rate — the
    /// default 30 keeps the volume at 30 Hz regardless of the frame rate,
    /// reducing the update cost of scenes with per-frame movable instances
    /// proportionally. The volume has no cross-frame feedback and the diffuse
    /// temporal resolve (~5-frame window) low-passes the quantized updates,
    /// so the only visible effect is indirect light from moving objects and
    /// lights lagging by up to 1/rate seconds. Invalid values (negative or
    /// non-finite) are clamped to 0.
    /// </summary>
    public float VolumeRefreshRate
    {
        get => _volumeRefreshRate;
        set => _volumeRefreshRate = float.IsFinite(value) ? MathF.Max(value, 0.0f) : 0.0f;
    }

    /// <summary>
    /// Gets or sets the RSM sun-bounce injection intensity (0 disables the
    /// injection in the trace shader). When set to 0 the owning
    /// <see cref="RGNode_RsmPass"/> must be disabled in the same frame (and
    /// vice versa): the RSM map is a graph transient written by that node, and
    /// the graph validates that this node only reads it while a writer is
    /// enabled — the read declaration itself is gated on this property.
    /// </summary>
    public float RsmInjectionIntensity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum world-space distance at which a diffuse cone
    /// march point still samples the RSM for sun bounce. Beyond it the march
    /// falls back to pure voxel radiance.
    /// </summary>
    public float RsmMaxDistance { get; set; } = 24.0f;

    /// <summary>
    /// Gets or sets the CSM cascade whose sun view defines the reflective
    /// shadow map (matching the cascade rendered by the app's
    /// <see cref="RGNode_RsmPass"/>). Coarser cascades cover more world space
    /// at lower texel density. Clamped to 0..3.
    /// </summary>
    public int RsmCascadeIndex
    {
        get => _rsmCascadeIndex;
        set => _rsmCascadeIndex = Math.Clamp(value, 0, RGNode_ShadowPass.CascadeCount - 1);
    }

    /// <summary>
    /// Gets or sets the minimum effective bounce albedo. RSM texels darker than
    /// this luminance are lifted to it in the shader so deeply dark surfaces
    /// still bounce a visible amount of sunlight.
    /// </summary>
    public float RsmMinAlbedo { get; set; } = 0.15f;

    /// <summary>Gets the RSM resolution in texels (tracked from the bound RSM map).</summary>
    public int RsmResolution { get; private set; } = 1024;

    /// <summary>Gets the most recently completed frame's GI diagnostics.</summary>
    public VoxelGiStatistics Statistics { get; private set; }

    /// <summary>
    /// The gathered indirect radiance atlas (five times the configured trace
    /// width: diffuse near/far layers, specular, ALD near/far layers with
    /// their view-linear layer depths in alpha). This is the trace-resolution
    /// atlas consumed by the internal upsample pass; the full-resolution outputs
    /// are <see cref="DiffuseTexture"/> and <see cref="SpecularTexture"/>.
    /// </summary>
    public RenderTexture IndirectTexture => _indirectAtlas;

    /// <summary>
    /// The full-resolution diffuse irradiance output (rgba = irradiance * directionalMod,
    /// selected visibility), produced by the internal upsample pass from
    /// <see cref="IndirectTexture"/>. Consumed by the deferred lighting pass.
    /// </summary>
    /// <exception cref="InvalidOperationException">The renderer is not attached to a graph.</exception>
    public RenderTexture DiffuseTexture => _giDiffuseFullRes
        ?? throw new InvalidOperationException("The voxel GI renderer is not attached to a graph (call Attach first).");

    /// <summary>
    /// The full-resolution specular radiance output, produced by the internal
    /// upsample pass. Consumed by the deferred lighting pass.
    /// </summary>
    /// <exception cref="InvalidOperationException">The renderer is not attached to a graph.</exception>
    public RenderTexture SpecularTexture => _giSpecularFullRes
        ?? throw new InvalidOperationException("The voxel GI renderer is not attached to a graph (call Attach first).");

    /// <summary>
    /// Create the voxel GI renderer.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="shaders">The complete set of voxel GI compute shaders.</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <param name="resolution">The voxel resolution of each clipmap level (power of two).</param>
    /// <param name="baseVoxelSize">The voxel size of the finest level in world units
    /// (default 0.25m, tuned on the Bistro scenes and fixed regardless of scene scale;
    /// with 128³ voxels per level the 4-level clipmap covers 32/64/128/256m).</param>
    /// <param name="traceResolutionScale">Screen-space cone-trace resolution relative to the G-buffer (0.25..1.0).</param>
    /// <exception cref="ArgumentException">The voxel resolution or trace-resolution scale is invalid.</exception>
    public RGNode_VoxelGI(
        RenderingSystem rendering,
        VoxelGiShaders shaders,
        uint width,
        uint height,
        int resolution = 128,
        float baseVoxelSize = 0.25f,
        float traceResolutionScale = 0.5f)
    {
        if (resolution < 16 || (resolution & (resolution - 1)) != 0)
        {
            throw new ArgumentException("The voxel resolution must be a power of two and at least 16.", nameof(resolution));
        }
        ValidateTraceResolutionScale(traceResolutionScale);

        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _resolution = resolution;
        _mipCount = (int)MathF.Log2(resolution) + 1;
        _baseVoxelSize = baseVoxelSize;
        _gbufferWidth = Math.Max(width, 1);
        _gbufferHeight = Math.Max(height, 1);
        _traceResolutionScale = traceResolutionScale;
        _clipmap = new VoxelGiClipmap(resolution, BrickSize, baseVoxelSize, LevelCount);

        _commandBuffer = _device.CreateCommandBuffer("voxel_gi");
        _clearMaterial = rendering.CreateComputeMaterial(shaders.Clear);
        _voxelizeMaterial = rendering.CreateComputeMaterial(shaders.Voxelize);
        _injectMaterial = rendering.CreateComputeMaterial(shaders.Inject);
        _mipMaterial = rendering.CreateComputeMaterial(shaders.Mip);
        _mipChainMaterial = rendering.CreateComputeMaterial(shaders.MipChain);
        _propagateMaterial = rendering.CreateComputeMaterial(shaders.Propagate);
        _traceMaterial = rendering.CreateComputeMaterial(shaders.Trace);
        _demosaicMaterial = rendering.CreateComputeMaterial(shaders.Demosaic);
        _dataBuffer = rendering.CreateGraphicsValueBuffer<VoxelGiData>("voxel_gi_data");

        // Persistent blue-noise lookup for the cone-march jitter (the same
        // tile the SSR trace samples): baked once on the first rendered frame
        // by a graphics pass, reused by the compute trace afterwards.
        _fullScreenMesh = rendering.MeshFullScreen;
        _blueNoiseMaterial = rendering.CreateMaterial(shaders.BlueNoise, "voxel_gi_blue_noise_bake");
        _blueNoiseLayout = _device.CreateAttachmentLayout(
            new AttachmentLayoutDescriptor(
                [new ColorAttachment(PixelFormat.RGBA8Unorm)],
                null,
                "voxel_gi_blue_noise_pass"));
        _blueNoiseTexture = rendering.CreateRenderTexture(
            _blueNoiseLayout, BlueNoiseTextureSize, BlueNoiseTextureSize, "voxel_gi_blue_noise");
        if (_device.TimestampQuerySupported)
        {
            _gpuTimestamps = new GpuTimestampSampler(_device, TimestampSlotCount, "voxel_gi");
        }

        _clearMaterial.SetBuffer("_data", _dataBuffer);
        _voxelizeMaterial.SetBuffer("_data", _dataBuffer);
        _injectMaterial.SetBuffer("_data", _dataBuffer);
        _mipMaterial.SetBuffer("_data", _dataBuffer);
        _mipChainMaterial.SetBuffer("_data", _dataBuffer);
        _propagateMaterial.SetBuffer("_data", _dataBuffer);
        _traceMaterial.SetBuffer("_data", _dataBuffer);
        _demosaicMaterial.SetBuffer("_data", _dataBuffer);
        // The blue-noise tile never changes after the bake, so bind it once.
        _traceMaterial.SetRenderTexture("_blueNoise", _blueNoiseTexture);

        // Attribute voxels are sparse physical 8^3 pages. Static data can fill
        // two complete levels and dynamic data one complete level before the
        // allocator starts dropping lower-priority far bricks.
        int bricksPerAxis = resolution / BrickSize;
        int pagesPerLevel = bricksPerAxis * bricksPerAxis * bricksPerAxis;
        int staticPageCapacity = pagesPerLevel * 2;
        int dynamicPageCapacity = pagesPerLevel;
        _staticPagePool = new VoxelGiPagePool(staticPageCapacity, LevelCount, resolution, BrickSize);
        _dynamicPagePool = new VoxelGiPagePool(dynamicPageCapacity, LevelCount, resolution, BrickSize);
        uint staticAttributeBytes = checked((uint)(_staticPagePool.VoxelCapacity * 16L));
        uint dynamicAttributeBytes = checked((uint)(_dynamicPagePool.VoxelCapacity * 16L));
        uint pageTableBytes = checked((uint)(pagesPerLevel * sizeof(uint)));
        uint dirtyBrickBytes = checked((uint)(pagesPerLevel * 16));

        _attrStatic = new GraphicsBuffer(rendering, staticAttributeBytes, "voxel_attr_static_pool");
        _attrDynamic = new GraphicsBuffer(rendering, dynamicAttributeBytes, "voxel_attr_dynamic_pool");
        _attributeMemoryBytes = staticAttributeBytes + dynamicAttributeBytes;

        for (int level = 0; level < LevelCount; level++)
        {
            _pageTableStatic[level] = new GraphicsBuffer(rendering, pageTableBytes, $"voxel_page_table_static_{level}");
            _pageTableDynamic[level] = new GraphicsBuffer(rendering, pageTableBytes, $"voxel_page_table_dynamic_{level}");
            _dirtyBrickCoordinates[level] = new GraphicsBuffer(rendering, dirtyBrickBytes, $"voxel_dirty_bricks_{level}");
            // Resident + stale brick list: worst case is 2× pagesPerLevel (all
            // bricks resident, all stale from a teleport). Each VoxelGiDirtyBrick
            // is 16 bytes.
            _residentBrickCoordinates[level] = new GraphicsBuffer(rendering, dirtyBrickBytes * 2, $"voxel_resident_bricks_{level}");
            // Combined page table: 2 uints per slot (static, dynamic).
            _pageTableCombined[level] = new GraphicsBuffer(rendering, pageTableBytes * 2, $"voxel_page_table_combined_{level}");
            _combinedPageTableScratch[level] = new uint[pagesPerLevel * 2];
            _currentResidentLogical[level] = new bool[pagesPerLevel];
            _previousResidentLogical[level] = new bool[pagesPerLevel];
        }

        // Double-buffered radiance: two RGBA16Float Texture3Ds with full mip
        // chains; all clipmap levels are stacked along the depth axis. Propagate
        // reads one and writes the other, alternating per bounce — no copy-back.
        _radiance[0] = rendering.CreateTexture3D((uint)resolution, (uint)resolution, (uint)(resolution * LevelCount),
            PixelFormat.RGBA16Float, (uint)_mipCount, name: "voxel_radiance_a");
        _radiance[1] = rendering.CreateTexture3D((uint)resolution, (uint)resolution, (uint)(resolution * LevelCount),
            PixelFormat.RGBA16Float, (uint)_mipCount, name: "voxel_radiance_b");

        // Directional opacity volume: xyz = |normal components| (anisotropic
        // occlusion), w = coverage. Full mip chain for cone-traced projection.
        // Single-buffered — propagate does not modify opacity.
        _opacity = rendering.CreateTexture3D((uint)resolution, (uint)resolution, (uint)(resolution * LevelCount),
            PixelFormat.RGBA16Float, (uint)_mipCount, name: "voxel_opacity");

        _radianceMemoryBytes = 2 * CalculateMipChainBytes(resolution, resolution, resolution * LevelCount, 8, _mipCount);

        // Initial bindings (rebound per-bounce in Render for propagate/trace):
        // Inject always writes to radiance[0] mip 0 at the start of each frame.
        _injectMaterial.SetTexture3DStorage("_radianceOut", _radiance[0], 0);
        _injectMaterial.SetTexture3DStorage("_opacityOut", _opacity, 0);
        _mipMaterial.SetTexture3DRead("_opacityLoad", _opacity, 0);
        _propagateMaterial.SetTexture("_opacity", _opacity);
        _traceMaterial.SetTexture("_opacity", _opacity);

        uint traceWidth = TraceWidth(_gbufferWidth);
        uint traceHeight = TraceHeight(_gbufferHeight);
        // Atlas: 5 segments (diffuse near/far, specular, ALD near/far).
        _indirectAtlas = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 5, traceHeight, "voxel_indirect_gi");
        // Trace raw: 3 segments (diffuse+visibility, specular, ALD).
        _traceRaw = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 3, traceHeight, "voxel_trace_raw_a");
        _traceHistory = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 3, traceHeight, "voxel_trace_raw_b");
        // History: 6 segments (diffuse near/far, specular, ALD near/far,
        // disocclusion metadata). The demosaic shader derives halfWidth as
        // traceRaw.Width / 3 (one segment width).
        _historyGI[0] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_a");
        _historyGI[1] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_b");
        _traceMaterial.SetRenderTexture("_indirectGI", _traceRaw);
        _traceMaterial.SetRenderTexture("_traceHistory", _traceHistory, 0);
        _traceMaterial.SetRenderTexture("_giHistoryMetadata", _historyGI[0], 0);
        _demosaicMaterial.SetRenderTexture("_traceInput", _traceRaw, 0);
        _demosaicMaterial.SetRenderTexture("_indirectGI", _indirectAtlas, 0);

        // The full-resolution GI outputs (upsampled from the trace-resolution
        // atlas by VoxelGiUpsample.hlsl, consumed by the deferred lighting pass)
        // are graph transients created by Attach.

        // Create the upsample compute pass eagerly when the shader is supplied.
        if (shaders.Upsample != null)
        {
            InitUpsample(shaders.Upsample);
        }

        // Bind the RSM fallback until Attach supplies a real RSM map.
        BindRsmTextures(null);
    }

    private void InitUpsample(Shader upsampleShader)
    {
        _upsampleMaterial = _rendering.CreateComputeMaterial(upsampleShader);
        _upsampleDataBuffer = _rendering.CreateGraphicsValueBuffer<VoxelGiUpsampleData>("voxel_gi_upsample_data");
        _upsampleMaterial.SetBuffer("_data", _upsampleDataBuffer);
        _upsampleMaterial.SetRenderTexture("_indirectGI", _indirectAtlas);
        // _giDiffuseOut/_giSpecularOut are bound by Attach once the graph
        // transients exist.
    }

    /// <summary>
    /// (Re)bind the trace pass's RSM slots. A null map binds the far-depth
    /// fallback (1x1 depth-only texture) plus the shared black textures, whose
    /// albedo alpha of 0 makes the shader's injection gate reject everything.
    /// Bindings only change when the map identity changes (resize-safe).
    /// </summary>
    private void BindRsmTextures(RenderTexture? rsmMap)
    {
        if (_rsmBound && ReferenceEquals(_boundRsmMap, rsmMap))
        {
            return;
        }
        _rsmBound = true;
        if (rsmMap != null)
        {
            _traceMaterial.SetRenderTextureDepth("_rsmDepth", rsmMap);
            _traceMaterial.SetRenderTexture("_rsmAlbedo", rsmMap, 0);
            _traceMaterial.SetRenderTexture("_rsmNormal", rsmMap, 1);
            RsmResolution = (int)rsmMap.Width;
        }
        else
        {
            // A depth-only 1x1 texture cleared once to far (1.0); its depth can
            // never match a receiver, and black albedo has alpha 0.
            _rsmFallbackDepth ??= _rendering.CreateRenderTexture(
                _rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
                    [], new DepthAttachment(PixelFormat.Depth32Float), "voxel_gi_rsm_fallback")),
                1, 1, "voxel_gi_rsm_fallback");
            _traceMaterial.SetRenderTextureDepth("_rsmDepth", _rsmFallbackDepth);
            _traceMaterial.SetTexture("_rsmAlbedo", _rendering.TextureBlack);
            _traceMaterial.SetTexture("_rsmNormal", _rendering.TextureBlack);
        }
        _boundRsmMap = rsmMap;
    }

    /// <summary>
    /// Attaches the renderer to a deferred composition as a direct
    /// <see cref="IRenderGraphNode"/> in the graph: creates the transient
    /// full-resolution outputs, registers itself immediately before the lighting
    /// node and wires the outputs to <see cref="RGNode_DeferredLighting.GiDiffuseInput"/> /
    /// <see cref="RGNode_DeferredLighting.GiSpecularInput"/> and the lighting material's
    /// GI slots. The graph rematerializes the outputs on resize. The trace and
    /// temporal-history textures stay persistent (cross-frame feedback never
    /// enters the graph).
    /// </summary>
    /// <param name="graph">The render graph driving the frame.</param>
    /// <param name="lighting">The deferred lighting node the GI outputs feed.</param>
    /// <param name="gbuffer">The G-buffer resource read by the trace passes.</param>
    /// <param name="shadowMap">The shadow map resource read by the inject pass.</param>
    /// <param name="environment">The shared scene environment (camera, lighting data
    /// and point lights).</param>
    /// <param name="rsmMap">Optional reflective shadow map transient (written by an
    /// enabled <see cref="RGNode_RsmPass"/> inserted earlier in the graph) driving the
    /// sun-bounce injection; null keeps the disabled fallback bindings.</param>
    /// <exception cref="InvalidOperationException">The renderer is already attached.</exception>
    public void Attach(RenderGraph graph, RGNode_DeferredLighting lighting, RenderGraphTexture gbuffer, RenderGraphTexture shadowMap, PBRSceneEnvironment environment, RenderGraphTexture? rsmMap = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentNullException.ThrowIfNull(gbuffer);
        ArgumentNullException.ThrowIfNull(shadowMap);
        ArgumentNullException.ThrowIfNull(environment);
        if (_graph != null)
        {
            throw new InvalidOperationException("The voxel GI renderer is already attached to a graph (call Detach first).");
        }
        _graph = graph;
        _lighting = lighting;
        _gbufferResource = gbuffer;
        _shadowMapResource = shadowMap;
        _environment = environment;
        _rsmMapResource = rsmMap;
        _giDiffuseResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "gi_diffuse"));
        _giSpecularResource = graph.CreateTransient(new RenderGraphTextureDescriptor(
            _rendering.PreferredLightMapPass, name: "gi_specular"));
        _giDiffuseFullRes = _giDiffuseResource.Texture;
        _giSpecularFullRes = _giSpecularResource.Texture;
        if (_upsampleMaterial != null)
        {
            _upsampleMaterial.SetRenderTexture("_giDiffuseOut", _giDiffuseFullRes);
            _upsampleMaterial.SetRenderTexture("_giSpecularOut", _giSpecularFullRes);
        }
        graph.InsertBefore(lighting, this);
        lighting.GiDiffuseInput = _giDiffuseResource;
        lighting.GiSpecularInput = _giSpecularResource;
        lighting.Material.SetRenderTexture("_giDiffuse", _giDiffuseFullRes);
        lighting.Material.SetRenderTexture("_giSpecular", _giSpecularFullRes);
    }

    /// <summary>
    /// Detaches the renderer from the graph: unregisters it, destroys its transient
    /// outputs and restores the lighting material's GI fallbacks. The renderer can
    /// be re-attached afterwards.
    /// </summary>
    public void Detach()
    {
        if (_graph == null)
        {
            return;
        }
        _graph.Remove(this);
        if (_giDiffuseResource != null)
        {
            _graph.DestroyTransient(_giDiffuseResource);
            _giDiffuseResource = null;
        }
        if (_giSpecularResource != null)
        {
            _graph.DestroyTransient(_giSpecularResource);
            _giSpecularResource = null;
        }
        _giDiffuseFullRes = null;
        _giSpecularFullRes = null;
        if (_lighting != null)
        {
            _lighting.GiDiffuseInput = null;
            _lighting.GiSpecularInput = null;
            _lighting.Material.SetTexture("_giDiffuse", _rendering.TextureBlack);
            _lighting.Material.SetTexture("_giSpecular", _rendering.TextureBlack);
        }
        _graph = null;
        _lighting = null;
        _gbufferResource = null;
        _shadowMapResource = null;
        _environment = null;
        _rsmMapResource = null;
        BindRsmTextures(null);
    }

    private static void ValidateTraceResolutionScale(float scale)
    {
        if (!float.IsFinite(scale) || scale < 0.25f || scale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "The GI trace-resolution scale must be between 0.25 and 1.0.");
        }
    }

    private uint TraceWidth(uint gbufferWidth)
        => Math.Max((uint)MathF.Ceiling(gbufferWidth * _traceResolutionScale), 1);

    private uint TraceHeight(uint gbufferHeight)
        => Math.Max((uint)MathF.Ceiling(gbufferHeight * _traceResolutionScale), 1);

    private static long CalculateMipChainBytes(int width, int height, int depth, int bytesPerVoxel, int mipCount)
    {
        long total = 0;
        for (int mip = 0; mip < mipCount; mip++)
        {
            total += (long)Math.Max(width >> mip, 1)
                * Math.Max(height >> mip, 1)
                * Math.Max(depth >> mip, 1)
                * bytesPerVoxel;
        }
        return total;
    }

    /// <summary>
    /// Registers a mesh and its GI material. Vertex and index data are copied once per
    /// source mesh and shared by all material registrations and instances.
    /// </summary>
    /// <param name="mesh">The single-submesh source mesh.</param>
    /// <param name="vertexStrideBytes">The vertex stride; position, normal and UV must be the first attributes.</param>
    /// <param name="localBounds">The mesh bounds before the instance transform.</param>
    /// <param name="albedo">The albedo texture, or null for white.</param>
    /// <param name="emissive">The emissive texture, or null for black.</param>
    /// <returns>A mesh-material handle accepted by static and dynamic instance methods.</returns>
    public int RegisterMesh(
        Mesh mesh,
        uint vertexStrideBytes,
        in VoxelGiBounds localBounds,
        Texture2D? albedo,
        Texture2D? emissive)
    {
        if (!_geometryByMesh.TryGetValue((mesh, vertexStrideBytes), out MeshGeometry? geometry))
        {
            geometry = CreateGeometry(mesh, vertexStrideBytes, localBounds);
            _geometryByMesh.Add((mesh, vertexStrideBytes), geometry);
            _geometries.Add(geometry);
        }

        _meshes.Add(new MeshRegistration
        {
            Geometry = geometry,
            Albedo = albedo,
            Emissive = emissive,
        });
        return _meshes.Count - 1;
    }

    /// <summary>Adds persistent structural geometry to the incrementally updated clipmap.</summary>
    /// <param name="meshHandle">The handle returned by <see cref="RegisterMesh"/>.</param>
    /// <param name="world">The local-to-world transform.</param>
    /// <param name="baseColor">The linear base color.</param>
    /// <param name="emissiveFactor">The linear emissive factor.</param>
    /// <param name="alphaCutoff">The alpha-test threshold; zero disables alpha testing.</param>
    /// <returns>A persistent instance handle.</returns>
    public int AddStaticInstance(
        int meshHandle,
        in Matrix4x4 world,
        in Vector4 baseColor,
        in Vector3 emissiveFactor,
        float alphaCutoff)
    {
        MeshRegistration registration = _meshes[meshHandle];
        VoxelGiBounds worldBounds = registration.Geometry.LocalBounds.Transform(world);
        var instance = new StaticInstance
        {
            Registration = registration,
            World = world,
            BaseColor = baseColor,
            Emissive = emissiveFactor,
            AlphaCutoff = alphaCutoff,
            WorldBounds = worldBounds,
            Active = true,
        };

        int handle;
        if (_freeStaticInstanceHandles.TryPop(out int freeHandle))
        {
            handle = freeHandle;
            _staticInstances[handle] = instance;
        }
        else
        {
            handle = _staticInstances.Count;
            _staticInstances.Add(instance);
        }
        InvalidateStatic(worldBounds);
        _staticBvhDirty = true;
        return handle;
    }

    /// <summary>Updates one structural instance and invalidates only its previous and new bounds.</summary>
    /// <param name="instanceHandle">The handle returned by <see cref="AddStaticInstance"/>.</param>
    /// <param name="world">The new local-to-world transform.</param>
    /// <param name="baseColor">The new linear base color.</param>
    /// <param name="emissiveFactor">The new linear emissive factor.</param>
    /// <param name="alphaCutoff">The new alpha-test threshold.</param>
    public void UpdateStaticInstance(
        int instanceHandle,
        in Matrix4x4 world,
        in Vector4 baseColor,
        in Vector3 emissiveFactor,
        float alphaCutoff)
    {
        StaticInstance instance = GetStaticInstance(instanceHandle);
        VoxelGiBounds previousBounds = instance.WorldBounds;
        instance.World = world;
        instance.BaseColor = baseColor;
        instance.Emissive = emissiveFactor;
        instance.AlphaCutoff = alphaCutoff;
        instance.WorldBounds = instance.Registration.Geometry.LocalBounds.Transform(world);
        InvalidateStatic(previousBounds);
        InvalidateStatic(instance.WorldBounds);
        _staticBvhDirty = true;
    }

    /// <summary>Removes one structural instance and schedules its occupied bricks for repair.</summary>
    /// <param name="instanceHandle">The handle returned by <see cref="AddStaticInstance"/>.</param>
    public void RemoveStaticInstance(int instanceHandle)
    {
        StaticInstance instance = GetStaticInstance(instanceHandle);
        InvalidateStatic(instance.WorldBounds);
        instance.Active = false;
        _staticInstances[instanceHandle] = null;
        _freeStaticInstanceHandles.Push(instanceHandle);
        _staticBvhDirty = true;
    }

    /// <summary>
    /// Submit one instance of a registered dynamic mesh for voxelization this
    /// frame. The instance list is consumed by <see cref="Render"/>.
    /// </summary>
    /// <param name="meshHandle">The handle returned by <see cref="RegisterMesh"/>.</param>
    /// <param name="world">The world transform of the instance.</param>
    /// <param name="baseColor">The linear base color, multiplied with the albedo texture.</param>
    /// <param name="emissiveFactor">The linear emissive factor, multiplied with the emissive texture.</param>
    /// <param name="alphaCutoff">Alpha test threshold; 0 disables alpha testing.</param>
    public void SubmitDynamicInstance(int meshHandle, in Matrix4x4 world, in Vector4 baseColor, in Vector3 emissiveFactor, float alphaCutoff)
    {
        MeshRegistration registration = _meshes[meshHandle];
        _instances.Add(new DynamicInstance
        {
            Registration = registration,
            World = world,
            BaseColor = baseColor,
            Emissive = emissiveFactor,
            AlphaCutoff = alphaCutoff,
            WorldBounds = registration.Geometry.LocalBounds.Transform(world),
        });
    }

    /// <summary>Removes every persistent structural instance while retaining shared mesh registrations.</summary>
    public void ClearStaticInstances()
    {
        _staticInstances.Clear();
        _freeStaticInstanceHandles.Clear();
        _staticBvhDirty = true;
        for (int level = 0; level < LevelCount; level++)
        {
            _staticNeedsFullClear[level] = true;
        }
        _clipmap.InvalidateAll();
    }

    /// <summary>Schedule a static re-voxelization of every clipmap level.</summary>
    public void InvalidateStatic()
    {
        for (int level = 0; level < LevelCount; level++)
        {
            _staticNeedsFullClear[level] = true;
        }
        _clipmap.InvalidateAll();
    }

    /// <summary>Invalidates structural voxel data overlapping world bounds.</summary>
    /// <param name="worldBounds">The edited or destroyed world-space region.</param>
    public void InvalidateStatic(in VoxelGiBounds worldBounds)
    {
        _clipmap.Invalidate(worldBounds);
    }

    /// <summary>
    /// Recreate the screen-space GI textures at a new G-buffer resolution using
    /// the current <see cref="TraceResolutionScale"/>.
    /// </summary>
    /// <param name="width">The new G-buffer width in pixels.</param>
    /// <param name="height">The new G-buffer height in pixels.</param>
    public void Resize(uint width, uint height)
    {
        _gbufferWidth = Math.Max(width, 1);
        _gbufferHeight = Math.Max(height, 1);
        uint traceWidth = TraceWidth(_gbufferWidth);
        uint traceHeight = TraceHeight(_gbufferHeight);

        // Allocate replacements before releasing the active textures so a
        // failed quality increase leaves the renderer usable.
        RenderTexture? newIndirectAtlas = null;
        RenderTexture? newTraceRaw = null;
        RenderTexture? newTraceHistory = null;
        RenderTexture? newHistoryA = null;
        RenderTexture? newHistoryB = null;
        try
        {
            newIndirectAtlas = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 5, traceHeight, "voxel_indirect_gi");
            newTraceRaw = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 3, traceHeight, "voxel_trace_raw_a");
            newTraceHistory = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 3, traceHeight, "voxel_trace_raw_b");
            newHistoryA = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_a");
            newHistoryB = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_b");
        }
        catch
        {
            newIndirectAtlas?.Dispose();
            newTraceRaw?.Dispose();
            newTraceHistory?.Dispose();
            newHistoryA?.Dispose();
            newHistoryB?.Dispose();
            throw;
        }

        _indirectAtlas.Dispose();
        _traceRaw.Dispose();
        _traceHistory.Dispose();
        _historyGI[0].Dispose();
        _historyGI[1].Dispose();

        _indirectAtlas = newIndirectAtlas;
        _traceRaw = newTraceRaw;
        _traceHistory = newTraceHistory;
        _historyGI[0] = newHistoryA;
        _historyGI[1] = newHistoryB;
        // The full-resolution outputs are graph transients, rematerialized by the
        // graph's own resize (the facades rebind through the render texture version
        // check), so only internal textures are recreated here.
        _traceMaterial.SetRenderTexture("_indirectGI", _traceRaw);
        _traceMaterial.SetRenderTexture("_traceHistory", _traceHistory, 0);
        _traceMaterial.SetRenderTexture("_giHistoryMetadata", _historyGI[0], 0);
        _demosaicMaterial.SetRenderTexture("_traceInput", _traceRaw, 0);
        _demosaicMaterial.SetRenderTexture("_indirectGI", _indirectAtlas, 0);
        if (_upsampleMaterial != null)
        {
            _upsampleMaterial.SetRenderTexture("_indirectGI", _indirectAtlas);
        }
        _historyReadIndex = 0;
        _historyValid = false;
        _boundGBuffer = null;
    }

    /// <summary>
    /// Run the hybrid GI passes: voxelize (static on dirty, dynamic every frame),
    /// inject direct lighting, rebuild radiance mips and gather diffuse/reflections
    /// from the G-buffer. Must be called after the G-buffer pass and before the
    /// lighting pass; dynamic instances are consumed (cleared) by the call.
    /// </summary>
    /// <param name="context">The render graph context providing the frame delta time.</param>
    private void Render(in RenderGraphContext context)
    {
        PBRSceneEnvironment environment = _environment!;
        CameraPerspectiveBuffer? camera = environment.Camera;
        if (camera == null)
        {
            throw new InvalidOperationException("VoxelGI requires a camera (set the environment's Camera first).");
        }
        Matrix4x4.Invert(camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);
        Transform3D cameraTransform = camera.Transform;
        RenderTexture gbuffer = _gbufferResource!.Texture;
        RenderTexture shadowMap = _shadowMapResource!.Texture;
        environment.AssembleLightingData(invViewProjection, gbuffer, giDiffuseActive: _giDiffuseResource != null);
        DeferredLightingData lightingData = environment.CurrentLightingData;
        GraphicsBuffer? pointLightBuffer = environment.PointLightBuffer;
        float deltaTime = context.DeltaTime;
        long recordStart = Stopwatch.GetTimestamp();
        int staticBricksUpdated = 0;
        int dynamicBricksUpdated = 0;
        int droppedBricks = 0;

        // Rate-limited volume update: the clipmap origins, movable-geometry
        // rebuild, injection, mip-chain rebuild and propagation only advance
        // VolumeRefreshRate times per second, so the radiance volume content
        // and the cbuffer origins used to sample it always stay in lockstep.
        // Structural voxelization is exempt: it runs every frame at a small
        // budget (see RunStaticVoxelize) and only fills attribute pages inside
        // the current window, which stays consistent because the origins only
        // advance on update frames. On skipped frames everything the trace
        // stage reads is frozen and mutually consistent — advancing the
        // origins every frame while the content lagged shifted the whole
        // indirect field by whole bricks between refreshes (visible as
        // flicker while the camera moves).
        _volumeUpdateElapsedSeconds += deltaTime;
        bool updateVolume = !_volumeInitialized
            || VolumeRefreshRate <= 0.0f
            || _volumeUpdateElapsedSeconds >= 1.0f / VolumeRefreshRate;
        if (updateVolume)
        {
            _clipmap.UpdateOrigins(cameraTransform.Position);
        }

        // ── Assemble the GPU constant buffer internally ──
        VoxelGiData data = new()
        {
            InvViewProjection = invViewProjection,
            CameraPosition = new Vector4(cameraTransform.Position, 0.0f),
        };

        // Copy lighting/shadow/sky data from the scene environment.
        DeferredLightingData ld = lightingData;
        data.SunViewProjection0 = ld.SunViewProjection0;
        data.SunViewProjection1 = ld.SunViewProjection1;
        data.SunViewProjection2 = ld.SunViewProjection2;
        data.SunViewProjection3 = ld.SunViewProjection3;
        data.SunDirection = ld.SunDirection;
        data.SunColorAndIntensity = ld.SunColorAndIntensity;
        data.SkyHorizonColor = ld.SkyHorizonColor;
        data.SkyZenithColor = ld.SkyZenithColor;
        data.CascadeSplits = ld.CascadeSplits;
        data.CascadeTexelSizes = ld.CascadeTexelSizes;
        // x=shadowEnabled y=numPointLights z=shadowMapSize w=rsmCascadeIndex
        data.LightingParams = new Vector4(
            ld.Params.X,
            ld.Params.Y,
            ld.Params.Z,
            RsmCascadeIndex);

        // Bind the point-light buffer once (the buffer is stable across frames).
        if (pointLightBuffer != null && !ReferenceEquals(_boundPointLightBuffer, pointLightBuffer))
        {
            _injectMaterial.SetBuffer(ShaderResourceId.PointLights, pointLightBuffer);
            _traceMaterial.SetBuffer(ShaderResourceId.PointLights, pointLightBuffer);
            _boundPointLightBuffer = pointLightBuffer;
        }

        // User-tunable GI parameters.
        if (!Matrix4x4.Invert(data.InvViewProjection, out data.ViewProjection))
        {
            data.ViewProjection = Matrix4x4.Identity;
        }
        data.ViewProjectionPrev = _viewProjectionPrev;

        data.LevelOrigin0 = _clipmap.GetOriginAndVoxelSize(0);
        data.LevelOrigin1 = _clipmap.GetOriginAndVoxelSize(1);
        data.LevelOrigin2 = _clipmap.GetOriginAndVoxelSize(2);
        data.LevelOrigin3 = _clipmap.GetOriginAndVoxelSize(3);
        data.LevelRingOffset0 = _clipmap.GetRingOffset(0);
        data.LevelRingOffset1 = _clipmap.GetRingOffset(1);
        data.LevelRingOffset2 = _clipmap.GetRingOffset(2);
        data.LevelRingOffset3 = _clipmap.GetRingOffset(3);
        data.ClipmapParams = new Vector4(_resolution, LevelCount, _mipCount, SsrOnly ? 0.0f : 1.0f);
        uint traceWidth = Math.Max(_traceRaw.Width / 3, 1);
        data.GiParams = new Vector4(EmissiveScale, TraceMaxDistance, traceWidth, _traceRaw.Height);
        data.GiParams2 = new Vector4((int)DebugView, gbuffer.Width, gbuffer.Height, SkyIntensity);
        data.GiFrameParams = new Vector4(_frameIndex, 0.05f, _historyValid ? 1.0f : 0.0f, DiffuseSpreading);

        // RSM sun bounce. The injection intensity is forced off when no map is
        // bound (detached / RSM pass never attached) even if the property says
        // otherwise, so the shader gate and the bindings stay consistent.
        RenderTexture? rsmMap = _rsmMapResource?.Texture;
        BindRsmTextures(rsmMap);
        float rsmIntensity = rsmMap != null ? RsmInjectionIntensity : 0.0f;
        // The shader sizes the glowing shell from its current march step
        // (never thinner than 1.5 fine voxels / RSM texels), so it only needs
        // the world-to-NDC-z scale of the cascade (depth range) and the RSM
        // texel's world size.
        float rsmTexelWorld = ld.CascadeTexelSizes[RsmCascadeIndex] * ld.Params.Z / MathF.Max(RsmResolution, 1);
        float rsmDepthRange = MathF.Max(environment.CascadeDepthRanges[RsmCascadeIndex], 1e-3f);
        data.RsmParams = new Vector4(rsmIntensity, RsmMaxDistance, rsmDepthRange, RsmMinAlbedo);
        data.RsmParams2 = new Vector4(RsmResolution, RsmResolution, rsmTexelWorld, 0.0f);
        _dataBuffer.UpdateBuffer(data);

        // The G-buffer and shadow map render textures are stable across frames
        // (recreated on resize); avoid rebinding every frame.
        if (!ReferenceEquals(_boundGBuffer, gbuffer))
        {
            _traceMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _traceMaterial.SetRenderTexture("_albedo", gbuffer, 0);
            _traceMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _traceMaterial.SetRenderTexture("_emissive", gbuffer, 3);
            _demosaicMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
            _demosaicMaterial.SetRenderTexture("_normal", gbuffer, 1);
            _demosaicMaterial.SetRenderTexture("_emissive", gbuffer, 3);
            if (_upsampleMaterial != null)
            {
                _upsampleMaterial.SetRenderTextureDepth("_gbufferDepth", gbuffer);
                _upsampleMaterial.SetRenderTexture("_normal", gbuffer, 1);
            }
            _boundGBuffer = gbuffer;
        }
        if (!ReferenceEquals(_boundShadowMap, shadowMap))
        {
            _injectMaterial.SetRenderTextureDepth("_shadowMap", shadowMap);
            _traceMaterial.SetRenderTextureDepth("_shadowMap", shadowMap);
            _boundShadowMap = shadowMap;
        }

        bool measureGpu = _gpuTimestamps != null && _gpuTimestamps.ShouldRecord;

        // Read back GPU timestamps from the previous sample (~1s ago — guaranteed
        // complete) and fold them into the running averages, but only when the
        // sampled frame ran the volume update: on skipped frames the update
        // stages are bracketed by back-to-back timestamps (~0 ms) and the pass
        // total shrinks by the update cost, so averaging those in would make
        // the counters oscillate at the beat of the 1 Hz sampler against the
        // refresh rate.
        if (measureGpu)
        {
            ulong[]? timestamps = _gpuTimestamps!.TryReadback();
            if (timestamps != null && _sampledVolumeUpdate)
            {
                AccumulateGpuDurations(timestamps);
            }
            // Remember whether this sample frame ran the volume update; the
            // timestamps recorded below are read back on the next sample frame.
            _sampledVolumeUpdate = updateVolume;
        }

        // Bake the blue-noise lookup once (procedural neighborhood-rank
        // construction, see ScreenSpaceReflectionBlueNoise.hlsl); every frame
        // afterwards the cone-trace march samples the persistent tile.
        if (!_blueNoiseBaked)
        {
            using RenderPassScope pass = context.RenderContext.BeginPass(_blueNoiseTexture.FrameBuffer);
            {
                pass.Draw(_fullScreenMesh, _blueNoiseMaterial);
            }
            _blueNoiseBaked = true;
        }

        // Record into the graph's frame-shared command buffer; the graph submits
        // it once at the end of the frame.
        GPUCommandBuffer commandBuffer = context.RenderContext.CommandBuffer;
        using (GPUCommandBuffer.ComputePass computePass = measureGpu
            ? commandBuffer.BeginCompute(_gpuTimestamps!.QuerySet, 0, 7)
            : commandBuffer.BeginCompute())
        {
            bool inPassTimestamps = _gpuTimestamps?.SupportsInPassTimestamps ?? false;
            int radianceReadIndex;

            // Structural voxelization is decoupled from the volume refresh
            // rate and runs every frame: draining the dirty-brick backlog at a
            // small per-frame budget spreads the cost of camera-scroll
            // invalidation across frames instead of bursting it all into the
            // update frame. Its output (attribute pages) is only consumed by
            // the inject stage on update frames, so this preserves the
            // origin/content lockstep documented above.
            RunStaticVoxelize(computePass, ref staticBricksUpdated, ref droppedBricks);
            if (!updateVolume)
            {
                // Bracket the skipped stages with back-to-back timestamps so
                // the profiler reports ~0 ms for them.
                if (measureGpu && inPassTimestamps)
                {
                    computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 1);
                    computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 2);
                    computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 3);
                    computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 4);
                }

                // The final radiance texture is deterministic for a fixed
                // bounce count: inject targets texture 0, each bounce flips.
                radianceReadIndex = Math.Max(0, BounceCount) & 1;
            }
            else
            {
                radianceReadIndex = RunVolumeUpdate(
                    computePass, measureGpu, inPassTimestamps,
                    ref dynamicBricksUpdated, ref droppedBricks);
                _volumeInitialized = true;
                // Carry the overshoot so non-divisor rates keep their average
                // (e.g. 45 Hz at 60 fps), clamped so a long frame causes at
                // most one immediately-follow-up update instead of a burst.
                if (VolumeRefreshRate > 0.0f)
                {
                    float interval = 1.0f / VolumeRefreshRate;
                    _volumeUpdateElapsedSeconds = MathF.Min(
                        _volumeUpdateElapsedSeconds - interval, interval);
                }
                else
                {
                    _volumeUpdateElapsedSeconds = 0.0f;
                }
            }

            if (measureGpu && inPassTimestamps)
            {
                computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 5);
            }

            // Gather rotation-balanced narrow-cone diffuse and specular from the
            // last-written radiance texture (direct + bounce).
            _traceMaterial.SetTexture("_radiance", _radiance[radianceReadIndex]);
            _traceMaterial.SetRenderTexture("_traceHistory", _traceHistory, 0);
            _traceMaterial.SetRenderTexture("_giHistoryMetadata", _historyGI[_historyReadIndex], 0);
            _traceMaterial.SetRenderTexture("_indirectGI", _traceRaw);
            _traceMaterial.DispatchBySize(computePass, traceWidth, _traceRaw.Height, 1);

            if (measureGpu && inPassTimestamps)
            {
                computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 6);
            }

            // Dual-layer diffuse resolve plus specular, one thread per trace
            // pixel writing all three atlas sections, followed by validated
            // per-layer history accumulation.
            int historyRead = _historyReadIndex;
            int historyWrite = 1 - historyRead;
            _demosaicMaterial.SetRenderTexture("_traceInput", _traceRaw, 0);
            _demosaicMaterial.SetRenderTexture("_historyInput", _historyGI[historyRead], 0);
            _demosaicMaterial.SetRenderTexture("_historyOut", _historyGI[historyWrite], 0);
            _demosaicMaterial.DispatchBySizeWithConstant(
                computePass,
                traceWidth,
                _traceRaw.Height,
                1,
                new Vector4(TemporalHysteresis, 1.0f, DiffuseTemporalHysteresis, 0.0f));
        }

        // Full-resolution upsample: read the trace-resolution atlas (_indirectAtlas),
        // blend near/far layers at full-resolution depth, apply ALD directional
        // modulation, and write two full-GBuffer-resolution textures consumed by
        // the deferred lighting pass. Separate compute pass ensures the demosaic
        // UAV writes are visible as SRV reads.
        if (_upsampleMaterial != null && _upsampleDataBuffer != null)
        {
            using GPUCommandBuffer.ComputePass upsamplePass = measureGpu
                ? commandBuffer.BeginCompute(_gpuTimestamps!.QuerySet, 8, 9)
                : commandBuffer.BeginCompute();
            _upsampleDataBuffer.Value.InvViewProjection = data.InvViewProjection;
            _upsampleDataBuffer.Value.Params = new Vector4(
                _gbufferWidth, _gbufferHeight,
                5.0f / _indirectAtlas.Width,
                1.0f / _indirectAtlas.Height);
            _upsampleDataBuffer.UpdateBuffer();
            _upsampleMaterial.DispatchBySize(upsamplePass, _gbufferWidth, _gbufferHeight, 1);
        }
        if (measureGpu)
        {
            commandBuffer.ResolveTimestamps(_gpuTimestamps!.QuerySet, 0, TimestampSlotCount, _gpuTimestamps.ResolveBuffer);
            _gpuTimestamps.EndSample();
        }

        _instances.Clear();
        _viewProjectionPrev = data.ViewProjection;
        _historyReadIndex = 1 - _historyReadIndex;
        (_traceRaw, _traceHistory) = (_traceHistory, _traceRaw);
        _historyValid = true;
        _frameIndex++;
        int pendingStaticBricks = 0;
        for (int level = 0; level < LevelCount; level++)
        {
            pendingStaticBricks += _clipmap.GetPendingBrickCount(level);
        }
        int activeStaticInstances = 0;
        for (int i = 0; i < _staticInstances.Count; i++)
        {
            if (_staticInstances[i] is { Active: true })
            {
                activeStaticInstances++;
            }
        }
        // Sum sparse dispatch statistics.
        int sparseBrickTotal = 0;
        int bricksPerLevel = _clipmap.BricksPerAxis * _clipmap.BricksPerAxis * _clipmap.BricksPerAxis;
        for (int level = 0; level < LevelCount; level++)
        {
            sparseBrickTotal += _residentCounts[level];
        }
        Statistics = new VoxelGiStatistics(
            _staticPagePool.AllocatedPageCount,
            _staticPagePool.Capacity,
            _dynamicPagePool.AllocatedPageCount,
            _dynamicPagePool.Capacity,
            pendingStaticBricks,
            staticBricksUpdated,
            dynamicBricksUpdated,
            droppedBricks,
            activeStaticInstances,
            _geometries.Count,
            _attributeMemoryBytes,
            _radianceMemoryBytes,
            Stopwatch.GetElapsedTime(recordStart).TotalMilliseconds,
            _gpuMilliseconds,
            sparseBrickTotal,
            bricksPerLevel * LevelCount);
    }

    /// <summary>
    /// Rebuilds the static-instance BVH when instances have been added, removed
    /// or updated since the last build. Compacts the sparse <see cref="_staticInstances"/>
    /// list (which has null slots for recycled handles) into a contiguous array of
    /// active bounds + references so <see cref="BvhAabb3D"/> leaf indices map
    /// directly to <see cref="_staticBvhInstances"/>.
    /// </summary>
    private void RebuildStaticBvhIfNeeded()
    {
        if (!_staticBvhDirty)
            return;
        _staticBvhDirty = false;
        _staticBvhBounds.Clear();
        _staticBvhInstances.Clear();
        for (int i = 0; i < _staticInstances.Count; i++)
        {
            StaticInstance? instance = _staticInstances[i];
            if (instance == null || !instance.Active)
                continue;
            _staticBvhBounds.Add(new BoundingBox3D(instance.WorldBounds.Min, instance.WorldBounds.Max));
            _staticBvhInstances.Add(instance);
        }
        _staticBvh.Build(CollectionsMarshal.AsSpan(_staticBvhBounds));
    }

    /// <summary>
    /// Structural voxelization, driven by high-priority edit bricks and
    /// lower-priority camera-streaming bricks. Runs every frame with a small
    /// per-level budget so camera-scroll invalidation is amortized across
    /// frames instead of bursting inside the rate-limited volume update; its
    /// output (attribute pages) is consumed solely by the inject stage on the
    /// next volume-update frame. The clipmap buffer is toroidal, so retained
    /// bricks survive camera movement without being copied.
    /// </summary>
    private void RunStaticVoxelize(
        GPUCommandBuffer.ComputePass computePass,
        ref int staticBricksUpdated,
        ref int droppedBricks)
    {
        RebuildStaticBvhIfNeeded();
        for (int level = 0; level < LevelCount; level++)
        {
            bool fullReset = _staticNeedsFullClear[level] || _clipmap.ConsumeFullReset(level);
            if (fullReset)
            {
                _staticPagePool.ResetLevel(level);
                _staticNeedsFullClear[level] = false;
            }

            int maximumBricks = Math.Clamp(
                _staticBrickBudgets[level],
                0,
                _clipmap.BricksPerAxis * _clipmap.BricksPerAxis * _clipmap.BricksPerAxis);
            int dirtyBrickCount = _clipmap.DrainDirtyBricks(level, maximumBricks, _dirtyBricks);
            if (dirtyBrickCount == 0)
            {
                if (fullReset)
                {
                    UploadPageTable(_pageTableStatic[level], _staticPagePool, level);
                }
                continue;
            }

            staticBricksUpdated += dirtyBrickCount;
            droppedBricks += UpdateStaticResidency(level, _dirtyBricks);
            UploadPageTable(_pageTableStatic[level], _staticPagePool, level);
            _dirtyBrickCoordinates[level].UpdateBuffer(CollectionsMarshal.AsSpan(_dirtyBricks));
            DispatchClearBricks(
                computePass,
                _attrStatic,
                _pageTableStatic[level],
                _dirtyBrickCoordinates[level],
                level,
                dirtyBrickCount);

            VoxelGiBounds dirtyBounds = GetDirtyBounds(level, _dirtyBricks);
            (uint dirtyRangeLo, uint dirtyRangeHi) = PackDirtyVoxelRange(_dirtyBricks);
            VoxelGiBounds levelBounds = _clipmap.GetLevelBounds(level);
            _staticBvhResults.Clear();
            _staticBvh.OverlapAabb(new BoundingBox3D(dirtyBounds.Min, dirtyBounds.Max), _staticBvhResults);
            for (int ri = 0; ri < _staticBvhResults.Count; ri++)
            {
                StaticInstance instance = _staticBvhInstances[_staticBvhResults[ri]];
                if (!instance.WorldBounds.Intersects(levelBounds))
                    continue;
                DispatchVoxelize(computePass, instance.Registration, _attrStatic, _pageTableStatic[level], level,
                    instance.World, instance.BaseColor, instance.Emissive, instance.AlphaCutoff,
                    dirtyRangeLo, dirtyRangeHi);
            }
        }
    }

    /// <summary>
    /// Rebuilds the movable-geometry pages, collects resident bricks, injects
    /// direct lighting, rebuilds the radiance mip chain and runs multi-bounce
    /// propagation (the Voxelize-tail through Propagate GPU stages). Returns
    /// the index into <see cref="_radiance"/> of the texture holding the final
    /// direct + bounce result for the trace stage.
    /// </summary>
    private int RunVolumeUpdate(
        GPUCommandBuffer.ComputePass computePass,
        bool measureGpu,
        bool inPassTimestamps,
        ref int dynamicBricksUpdated,
        ref int droppedBricks)
    {
        // Build the dynamic-instance BVH once per frame; all per-level queries
        // (CollectDynamicBricks and voxelize dispatch) share this tree. Morton
        // LBVH rebuild is sub-millisecond for typical instance counts.
        _dynamicBvhBounds.Clear();
        for (int i = 0; i < _instances.Count; i++)
        {
            VoxelGiBounds wb = _instances[i].WorldBounds;
            _dynamicBvhBounds.Add(new BoundingBox3D(wb.Min, wb.Max));
        }
        _dynamicBvh.Build(CollectionsMarshal.AsSpan(_dynamicBvhBounds));

        // Movable geometry is rebuilt in a separate sparse pool each frame
        // and limited to the nearest configured clipmap levels.
        _dynamicPagePool.Reset();
        int dynamicLevelCount = Math.Clamp(DynamicLevelCount, 0, LevelCount);
        for (int level = 0; level < LevelCount; level++)
        {
            bool active = level < dynamicLevelCount;
            if (!active)
            {
                UploadPageTable(_pageTableDynamic[level], _dynamicPagePool, level);
                continue;
            }

            CollectDynamicBricks(level);
            dynamicBricksUpdated += _dirtyBricks.Count;
            for (int brickIndex = 0; brickIndex < _dirtyBricks.Count; brickIndex++)
            {
                if (!_dynamicPagePool.TrySetResident(
                    level,
                    _dirtyBricks[brickIndex],
                    _clipmap.GetRingOffset(level),
                    true))
                {
                    droppedBricks++;
                }
            }
            UploadPageTable(_pageTableDynamic[level], _dynamicPagePool, level);
            if (_dirtyBricks.Count > 0)
            {
                _dirtyBrickCoordinates[level].UpdateBuffer(CollectionsMarshal.AsSpan(_dirtyBricks));
                DispatchClearBricks(
                    computePass,
                    _attrDynamic,
                    _pageTableDynamic[level],
                    _dirtyBrickCoordinates[level],
                    level,
                    _dirtyBricks.Count);

                // No collected bricks means no resident pages at all, so the
                // instance scan is skipped entirely instead of dispatching full
                // triangle counts that can write nowhere.
                (uint dirtyRangeLo, uint dirtyRangeHi) = PackDirtyVoxelRange(_dirtyBricks);
                VoxelGiBounds levelBounds = _clipmap.GetLevelBounds(level);
                _dynamicBvhResults.Clear();
                _dynamicBvh.OverlapAabb(new BoundingBox3D(levelBounds.Min, levelBounds.Max), _dynamicBvhResults);
                for (int ri = 0; ri < _dynamicBvhResults.Count; ri++)
                {
                    DynamicInstance instance = _instances[_dynamicBvhResults[ri]];
                    DispatchVoxelize(computePass, instance.Registration, _attrDynamic, _pageTableDynamic[level], level,
                        instance.World, instance.BaseColor, instance.Emissive, instance.AlphaCutoff,
                        dirtyRangeLo, dirtyRangeHi);
                }
            }
        }

        if (measureGpu && inPassTimestamps)
        {
            computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 1);
        }

        // Collect resident brick lists for sparse inject/propagate.
        // The buffer layout per level is [resident bricks..., stale bricks...].
        // Inject dispatches over both (stale bricks get zeroed); Propagate
        // only over the resident portion.
        for (int level = 0; level < LevelCount; level++)
        {
            CollectResidentBricks(level);
            if (_residentBricks.Count > 0)
            {
                _residentBrickCoordinates[level].UpdateBuffer(
                    CollectionsMarshal.AsSpan(_residentBricks));
            }
        }

        // Direct lighting injection into radiance mip 0 (sparse).
        for (int level = 0; level < LevelCount; level++)
        {
            int injectCount = _residentCounts[level] + _staleCounts[level];
            if (injectCount == 0)
            {
                continue;
            }
            _injectMaterial.SetBuffer("_attrStatic", _attrStatic);
            _injectMaterial.SetBuffer("_attrDynamic", _attrDynamic);
            _injectMaterial.SetBuffer("_pageTable", _pageTableCombined[level]);
            _injectMaterial.SetBuffer("_brickList", _residentBrickCoordinates[level]);
            _injectMaterial.DispatchBySizeWithConstant(
                computePass, BrickSize, BrickSize, (uint)(BrickSize * injectCount),
                new Vector4(level, 0, 0, 0));
        }

        if (measureGpu && inPassTimestamps)
        {
            computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 2);
        }

        // Build mip chain after injection so the propagation cones sample
        // correct coarse-mip data instead of stale values from the previous
        // frame. Without this, the first bounce gathers against an empty or
        // outdated radiance volume.
        int radianceReadIndex = 0;
        BuildMipChains(computePass, _radiance[radianceReadIndex]);

        if (measureGpu && inPassTimestamps)
        {
            computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 3);
        }

        // Multi-bounce light propagation (sparse, double-buffered): each
        // occupied voxel traces a cone set through the source radiance volume
        // to gather indirect light, multiplies by albedo and adds to existing
        // direct radiance, then writes directly into the alternate radiance
        // texture's mip 0. No separate copy-back pass is needed. Iterated
        // BounceCount times, alternating read/write textures each bounce so
        // bounce N+1 sees bounce N's results.
        int bounceCount = Math.Max(0, BounceCount);
        for (int bounce = 0; bounce < bounceCount; bounce++)
        {
            int writeIndex = 1 - radianceReadIndex;
            _propagateMaterial.SetTexture("_radiance", _radiance[radianceReadIndex]);
            _propagateMaterial.SetTexture3DStorage("_propagateOut", _radiance[writeIndex], 0);

            for (int level = 0; level < LevelCount; level++)
            {
                int residentCount = _residentCounts[level];
                if (residentCount == 0)
                {
                    continue;
                }
                _propagateMaterial.SetBuffer("_attrStatic", _attrStatic);
                _propagateMaterial.SetBuffer("_attrDynamic", _attrDynamic);
                _propagateMaterial.SetBuffer("_pageTable", _pageTableCombined[level]);
                _propagateMaterial.SetBuffer("_brickList", _residentBrickCoordinates[level]);
                _propagateMaterial.DispatchBySizeWithConstant(
                    computePass, BrickSize, BrickSize, (uint)(BrickSize * residentCount),
                    new Vector4(level, BounceStrength, bounce, 0));
            }

            // Build mip chain on the write texture so the next bounce (or
            // the screen-space tracer) samples correct coarse-mip data.
            BuildMipChains(computePass, _radiance[writeIndex]);
            radianceReadIndex = writeIndex;
        }

        if (measureGpu && inPassTimestamps)
        {
            computePass.WriteTimestamp(_gpuTimestamps!.QuerySet, 4);
        }

        return radianceReadIndex;
    }

    /// <summary>
    /// Fold the GPU durations of one sampled volume-update frame into the
    /// exponential moving averages backing the profiler counters
    /// (<see cref="_gpuMilliseconds"/>, <see cref="_stageGpuMilliseconds"/>).
    /// Only update frames contribute — skipped frames record back-to-back
    /// timestamps (~0 ms) for the update stages. Slots 0–7 bracket the main
    /// compute pass (0=begin, 7=end), slots 8–9 bracket the upsample pass.
    /// In-pass slots 1–6 are no-ops (read as 0) when the device lacks
    /// <see cref="GPUDevice.TimestampQueryInsidePassesSupported"/>.
    /// </summary>
    private void AccumulateGpuDurations(ulong[] timestamps)
    {
        GpuTimestampSampler ring = _gpuTimestamps!;
        // Blend factor per sample; samples arrive at the sampler's ~1 Hz
        // cadence, so the averages track changes over a few seconds.
        const double alpha = 0.25;
        double total = ring.DeltaMilliseconds(timestamps, 0, 7);
        if (!_gpuAveragesPrimed)
        {
            _gpuMilliseconds = total;
            for (int i = 0; i < GiStageCount; i++)
            {
                _stageGpuMilliseconds[i] = StageGpuDuration(ring, timestamps, i);
            }
            _gpuAveragesPrimed = true;
            return;
        }
        _gpuMilliseconds += (total - _gpuMilliseconds) * alpha;
        for (int i = 0; i < GiStageCount; i++)
        {
            double duration = StageGpuDuration(ring, timestamps, i);
            _stageGpuMilliseconds[i] += (duration - _stageGpuMilliseconds[i]) * alpha;
        }
    }

    /// <summary>
    /// Returns the GPU duration of one stage in milliseconds from a resolved
    /// timestamp array: 0=Voxelize (0→1), 1=Inject (1→2), 2=Inject MipChain
    /// (2→3), 3=Propagate (3→4), 4=Trace (5→6),
    /// 5=Demosaic (6→7), 6=Upsample (8→9).
    /// </summary>
    private static double StageGpuDuration(GpuTimestampSampler ring, ulong[] timestamps, int stage)
        => stage switch
        {
            0 => ring.DeltaMilliseconds(timestamps, 0, 1),
            1 => ring.DeltaMilliseconds(timestamps, 1, 2),
            2 => ring.DeltaMilliseconds(timestamps, 2, 3),
            3 => ring.DeltaMilliseconds(timestamps, 3, 4),
            4 => ring.DeltaMilliseconds(timestamps, 5, 6),
            5 => ring.DeltaMilliseconds(timestamps, 6, 7),
            _ => ring.DeltaMilliseconds(timestamps, 8, 9),
        };

    /// <inheritdoc />
    void IRenderGraphNode.Setup(RenderGraphBuilder builder)
    {
        builder.Read(_gbufferResource!);
        if (_environment!.ShadowEnabled)
        {
            builder.Read(_shadowMapResource!);
        }
        // Gated on the intensity so disabling the injection (which must disable
        // the app's RSM pass node in lockstep) does not leave this node reading
        // a transient no enabled node writes this frame.
        if (_rsmMapResource != null && RsmInjectionIntensity > 0.0f)
        {
            builder.Read(_rsmMapResource);
        }
        builder.Write(_giDiffuseResource!);
        builder.Write(_giSpecularResource!);
    }

    /// <inheritdoc />
    void IRenderGraphNode.Execute(in RenderGraphContext context)
    {
        Render(context);

        RenderProfiler? profiler = _graph?.Profiler;
        if (profiler == null)
        {
            return;
        }

        // Lazily register profiler counters on the first Execute call.
        if (!_profilerCountersRegistered)
        {
            _giTotalCounter = profiler.RegisterCounter("VoxelGI", "Total (CPU)");
            _giGpuCounter = profiler.RegisterCounter("VoxelGI", "GPU");
            _giStageCounters[0] = profiler.RegisterCounter("VoxelGI", "Voxelize");
            _giStageCounters[1] = profiler.RegisterCounter("VoxelGI", "Inject");
            _giStageCounters[2] = profiler.RegisterCounter("VoxelGI", "Inject MipChain");
            _giStageCounters[3] = profiler.RegisterCounter("VoxelGI", "Propagate");
            _giStageCounters[4] = profiler.RegisterCounter("VoxelGI", "Trace");
            _giStageCounters[5] = profiler.RegisterCounter("VoxelGI", "Demosaic");
            _giStageCounters[6] = profiler.RegisterCounter("VoxelGI", "Upsample");
            _profilerCountersRegistered = true;
        }
        profiler.PushValue(_giTotalCounter, Statistics.CpuRecordMilliseconds);
        profiler.PushValue(_giGpuCounter, Statistics.GpuMilliseconds);
        for (int i = 0; i < GiStageCount; i++)
        {
            profiler.PushValue(_giStageCounters[i], _stageGpuMilliseconds[i]);
        }
    }

    private int UpdateStaticResidency(int level, List<VoxelGiDirtyBrick> bricks)
    {
        int droppedBricks = 0;
        Vector4 ringOffset = _clipmap.GetRingOffset(level);
        for (int brickIndex = 0; brickIndex < bricks.Count; brickIndex++)
        {
            VoxelGiDirtyBrick brick = bricks[brickIndex];
            VoxelGiBounds brickBounds = _clipmap.GetBrickBounds(level, brick);
            bool resident = HasStaticGeometry(brickBounds);
            if (!_staticPagePool.TrySetResident(level, brick, ringOffset, resident))
            {
                droppedBricks++;
                _clipmap.RequeueDirtyBrick(level, brick);
            }
        }
        return droppedBricks;
    }

    private bool HasStaticGeometry(in VoxelGiBounds bounds)
    {
        _staticBvhResults.Clear();
        _staticBvh.OverlapAabb(new BoundingBox3D(bounds.Min, bounds.Max), _staticBvhResults);
        return _staticBvhResults.Count > 0;
    }

    private void CollectDynamicBricks(int level)
    {
        _dirtyBricks.Clear();
        _brickKeys.Clear();
        VoxelGiBounds levelBounds = _clipmap.GetLevelBounds(level);
        _dynamicBvhResults.Clear();
        _dynamicBvh.OverlapAabb(new BoundingBox3D(levelBounds.Min, levelBounds.Max), _dynamicBvhResults);
        for (int ri = 0; ri < _dynamicBvhResults.Count; ri++)
        {
            DynamicInstance instance = _instances[_dynamicBvhResults[ri]];

            _candidateBricks.Clear();
            _clipmap.AppendIntersectingBricks(level, instance.WorldBounds, _candidateBricks);
            for (int brickIndex = 0; brickIndex < _candidateBricks.Count; brickIndex++)
            {
                VoxelGiDirtyBrick brick = _candidateBricks[brickIndex];
                uint key = brick.X
                    + brick.Y * (uint)_clipmap.BricksPerAxis
                    + brick.Z * (uint)(_clipmap.BricksPerAxis * _clipmap.BricksPerAxis);
                if (_brickKeys.Add(key))
                {
                    _dirtyBricks.Add(brick);
                }
            }
        }
    }

    /// <summary>
    /// Collects the per-level resident brick list (union of static + dynamic
    /// page tables) plus any bricks that were resident last frame but are no
    /// longer (stale). Stale bricks are appended so the inject pass can clear
    /// their stale radiance by writing zeros, avoiding a separate full-pass
    /// clear. Returns via <see cref="_residentBricks"/> (resident+stale) and
    /// <see cref="_residentCounts"/>/<see cref="_staleCounts"/>.
    /// </summary>
    private void CollectResidentBricks(int level)
    {
        _residentBricks.Clear();
        _staleBricks.Clear();

        int bpa = _resolution / BrickSize;
        ReadOnlySpan<uint> staticTable = _staticPagePool.GetPageTable(level);
        ReadOnlySpan<uint> dynamicTable = _dynamicPagePool.GetPageTable(level);
        bool[] currentSeen = _currentResidentLogical[level];
        Array.Clear(currentSeen, 0, currentSeen.Length);

        Vector4 ringOffset = _clipmap.GetRingOffset(level);
        int ringBrickX = (int)ringOffset.X / BrickSize;
        int ringBrickY = (int)ringOffset.Y / BrickSize;
        int ringBrickZ = (int)ringOffset.Z / BrickSize;

        // Single pass: collect resident bricks and build the combined page
        // table (interleaved static, dynamic) in one loop over 4096 slots.
        uint[] combined = _combinedPageTableScratch[level];
        for (int slot = 0; slot < staticTable.Length; slot++)
        {
            uint staticEntry = staticTable[slot];
            uint dynamicEntry = dynamicTable[slot];
            combined[slot * 2] = staticEntry;
            combined[slot * 2 + 1] = dynamicEntry;

            if (staticEntry == 0u && dynamicEntry == 0u)
            {
                continue;
            }

            // Decode toroidal slot → logical brick coordinate.
            int pz = slot / (bpa * bpa);
            int rem = slot % (bpa * bpa);
            int py = rem / bpa;
            int px = rem % bpa;
            int lx = ((px - ringBrickX) % bpa + bpa) % bpa;
            int ly = ((py - ringBrickY) % bpa + bpa) % bpa;
            int lz = ((pz - ringBrickZ) % bpa + bpa) % bpa;

            int logicalIndex = lx + ly * bpa + lz * bpa * bpa;
            currentSeen[logicalIndex] = true;
            _residentBricks.Add(new VoxelGiDirtyBrick((uint)lx, (uint)ly, (uint)lz));
        }

        // Detect stale logical positions (resident last frame, not this frame).
        bool[] previousSeen = _previousResidentLogical[level];
        for (int i = 0; i < previousSeen.Length; i++)
        {
            if (previousSeen[i] && !currentSeen[i])
            {
                int lz = i / (bpa * bpa);
                int rem = i % (bpa * bpa);
                int ly = rem / bpa;
                int lx = rem % bpa;
                _staleBricks.Add(new VoxelGiDirtyBrick((uint)lx, (uint)ly, (uint)lz));
            }
        }

        // Remember current residents for next frame's stale detection.
        Array.Copy(currentSeen, previousSeen, currentSeen.Length);

        _residentCounts[level] = _residentBricks.Count;
        _staleCounts[level] = _staleBricks.Count;

        _pageTableCombined[level].UpdateBuffer<uint>(combined);

        // Concatenate stale bricks after resident bricks in the upload buffer.
        // Inject dispatches over both; Propagate only over resident.
        _residentBricks.AddRange(_staleBricks);
    }

    private static void UploadPageTable(GraphicsBuffer buffer, VoxelGiPagePool pagePool, int level)
    {
        buffer.UpdateBuffer(pagePool.GetPageTable(level));
    }

    /// <summary>
    /// Builds the full radiance + opacity mip chain (mip 0 → mip N) for all
    /// clipmap levels. Each transition reads mip N and writes mip N+1 through
    /// single-mip views whose non-overlapping subresource ranges avoid the
    /// read/write usage conflict within one dispatch.
    /// Levels are packed into the dispatch z dimension, so one dispatch per
    /// transition covers all clipmap levels. The last three transitions (tiny
    /// mip sizes) are merged into a single cascading dispatch per texture type
    /// via VoxelMipChain, reducing dispatch count further.
    /// </summary>
    private void BuildMipChains(GPUCommandBuffer.ComputePass computePass, Texture3D radiance)
    {
        // The last 3 transitions are cascaded; earlier transitions use the
        // standard per-mip shader.
        int cascadeSrcMip = Math.Max(0, _mipCount - 4);

        for (int mip = 0; mip < cascadeSrcMip; mip++)
        {
            _mipMaterial.SetTexture3DRead("_radianceLoad", radiance, (uint)mip);
            _mipMaterial.SetTexture3DStorage("_radianceOut", radiance, (uint)(mip + 1));
            _mipMaterial.SetTexture3DRead("_opacityLoad", _opacity, (uint)mip);
            _mipMaterial.SetTexture3DStorage("_opacityOut", _opacity, (uint)(mip + 1));
            uint dstResolution = (uint)Math.Max(_resolution >> (mip + 1), 1);
            // All levels in one dispatch: z = dstRes * LevelCount.
            _mipMaterial.DispatchBySizeWithConstant(
                computePass, dstResolution, dstResolution, dstResolution * (uint)LevelCount,
                new Vector4(mip, 0, 0, 0));
        }

        // Cascade the last 3 transitions (radiance then opacity) in one dispatch
        // each, using groupshared reductions inside the shader.
        DispatchMipChain(computePass, radiance, cascadeSrcMip, 0);
        DispatchMipChain(computePass, _opacity, cascadeSrcMip, 1);
    }

    /// <summary>
    /// Dispatches the cascading mip-chain shader for one texture (radiance or
    /// opacity), producing three coarser mips from a single source mip.
    /// </summary>
    private void DispatchMipChain(
        GPUCommandBuffer.ComputePass computePass, Texture3D texture, int srcMip, int mode)
    {
        _mipChainMaterial.SetTexture3DRead("_srcTex", texture, (uint)srcMip);
        _mipChainMaterial.SetTexture3DStorage("_outTex1", texture, (uint)(srcMip + 1));
        _mipChainMaterial.SetTexture3DStorage("_outTex2", texture, (uint)(srcMip + 2));
        _mipChainMaterial.SetTexture3DStorage("_outTex3", texture, (uint)(srcMip + 3));
        // 4 × 4 × (4 * LevelCount) threads: one 4³ group per clipmap level.
        _mipChainMaterial.DispatchBySizeWithConstant(
            computePass, 4, 4, 4 * (uint)LevelCount,
            new Vector4(srcMip, mode, 0, 0));
    }

    private void DispatchClearBricks(
        GPUCommandBuffer.ComputePass computePass,
        GraphicsBuffer attributes,
        GraphicsBuffer pageTable,
        GraphicsBuffer dirtyBricks,
        int level,
        int brickCount)
    {
        _clearMaterial.SetBuffer("_attrOut", attributes);
        _clearMaterial.SetBuffer("_dirtyBricks", dirtyBricks);
        _clearMaterial.SetBuffer("_pageTable", pageTable);
        _clearMaterial.DispatchBySizeWithConstant(
            computePass,
            BrickSize,
            BrickSize,
            (uint)(BrickSize * brickCount),
            new Vector4(level, 0.0f, 0.0f, 0.0f));
    }

    private void DispatchVoxelize(
        GPUCommandBuffer.ComputePass computePass,
        MeshRegistration registration,
        GraphicsBuffer attrOut,
        GraphicsBuffer pageTable,
        int level,
        in Matrix4x4 world,
        in Vector4 baseColor,
        in Vector3 emissive,
        float alphaCutoff,
        uint dirtyRangeLo,
        uint dirtyRangeHi)
    {
        MeshGeometry geometry = registration.Geometry;
        if (geometry.TriangleCount == 0)
        {
            return;
        }

        _voxelizeMaterial.SetBuffer("_vertices", geometry.Vertices);
        _voxelizeMaterial.SetBuffer("_indices", geometry.Indices);
        _voxelizeMaterial.SetBuffer("_attrOut", attrOut);
        _voxelizeMaterial.SetBuffer("_pageTable", pageTable);
        _voxelizeMaterial.SetTexture("_albedoTexture", registration.Albedo ?? _rendering.TextureWhite);
        _voxelizeMaterial.SetTexture("_emissiveTexture", registration.Emissive ?? _rendering.TextureBlack);
        _voxelizeMaterial.DispatchBySizeWithConstant(computePass, geometry.TriangleCount, 8, 1, new VoxelizeConstants
        {
            Model = world,
            BaseColor = baseColor,
            Emissive = new Vector4(emissive, 0.0f),
            Params = new Vector4(level, geometry.Index16Bit ? 1.0f : 0.0f, geometry.VertexStrideUints, alphaCutoff),
            Params2 = new Vector4(
                geometry.TriangleCount,
                BitConverter.Int32BitsToSingle((int)dirtyRangeLo),
                BitConverter.Int32BitsToSingle((int)dirtyRangeHi),
                0.0f),
        });
    }

    private MeshGeometry CreateGeometry(Mesh mesh, uint vertexStrideBytes, in VoxelGiBounds localBounds)
    {
        SubMeshData subMesh = mesh.GetSubMesh(0);
        uint vertexBytes = mesh.VertexBuffer.Size;
        uint indexBytes = mesh.IndexBuffer.Size;

        var geometry = new MeshGeometry
        {
            Vertices = new GraphicsBuffer(_rendering, vertexBytes, $"voxel_vertices_{mesh.Name}"),
            Indices = new GraphicsBuffer(_rendering, indexBytes, $"voxel_indices_{mesh.Name}"),
            LocalBounds = localBounds,
            TriangleCount = subMesh.IndexCount / 3,
            VertexStrideUints = vertexStrideBytes / 4,
            Index16Bit = subMesh.IndexFormat == IndexFormat.UInt16,
        };

        // Copy the mesh data into the voxelization buffers (mesh buffers are
        // created with CopySrc usage for this).
        _commandBuffer.Begin();
        _commandBuffer.CopyBuffer(mesh.VertexBuffer, geometry.Vertices.NativeBuffer, vertexBytes);
        _commandBuffer.CopyBuffer(mesh.IndexBuffer, geometry.Indices.NativeBuffer, indexBytes);
        _commandBuffer.End();
        _device.Submit(_commandBuffer);

        return geometry;
    }

    private StaticInstance GetStaticInstance(int handle)
    {
        if ((uint)handle >= (uint)_staticInstances.Count
            || _staticInstances[handle] is not StaticInstance instance
            || !instance.Active)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "The static GI instance handle is not active.");
        }
        return instance;
    }

    private VoxelGiBounds GetDirtyBounds(int level, List<VoxelGiDirtyBrick> bricks)
    {
        VoxelGiBounds bounds = _clipmap.GetBrickBounds(level, bricks[0]);
        for (int i = 1; i < bricks.Count; i++)
        {
            bounds = bounds.Union(_clipmap.GetBrickBounds(level, bricks[i]));
        }
        return bounds;
    }

    /// <summary>
    /// Packs the inclusive voxel-space range covered by the dirty brick list into
    /// two words (x | y&lt;&lt;8 | z&lt;&lt;16 each) for the voxelize shader's loop
    /// clamp. Voxel coordinates fit in 8 bits per axis (clipmap resolution ≤ 256).
    /// </summary>
    private static (uint Lo, uint Hi) PackDirtyVoxelRange(List<VoxelGiDirtyBrick> bricks)
    {
        uint minX = uint.MaxValue, minY = uint.MaxValue, minZ = uint.MaxValue;
        uint maxX = 0, maxY = 0, maxZ = 0;
        for (int i = 0; i < bricks.Count; i++)
        {
            VoxelGiDirtyBrick brick = bricks[i];
            minX = Math.Min(minX, brick.X);
            minY = Math.Min(minY, brick.Y);
            minZ = Math.Min(minZ, brick.Z);
            maxX = Math.Max(maxX, brick.X);
            maxY = Math.Max(maxY, brick.Y);
            maxZ = Math.Max(maxZ, brick.Z);
        }
        uint lo = minX * BrickSize | (minY * BrickSize << 8) | (minZ * BrickSize << 16);
        uint hi = (maxX * BrickSize + BrickSize - 1)
            | ((maxY * BrickSize + BrickSize - 1) << 8)
            | ((maxZ * BrickSize + BrickSize - 1) << 16);
        return (lo, hi);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            for (int i = 0; i < _geometries.Count; i++)
            {
                _geometries[i].Vertices.Dispose();
                _geometries[i].Indices.Dispose();
            }
            for (int level = 0; level < LevelCount; level++)
            {
                _pageTableStatic[level].Dispose();
                _pageTableDynamic[level].Dispose();
                _dirtyBrickCoordinates[level].Dispose();
                _residentBrickCoordinates[level].Dispose();
                _pageTableCombined[level].Dispose();
            }
            _attrStatic.Dispose();
            _attrDynamic.Dispose();
            _radiance[0].Dispose();
            _radiance[1].Dispose();
            _opacity.Dispose();
            _traceRaw.Dispose();
            _traceHistory.Dispose();
            _indirectAtlas.Dispose();
            // The full-resolution GI outputs are graph-owned facades, disposed
            // with the graph. Compute materials hold no native resources of
            // their own and are not disposable.
            _historyGI[0].Dispose();
            _historyGI[1].Dispose();
            // Unlike the compute materials, the graphics bake material owns
            // pass state and must be disposed with its texture and layout.
            _blueNoiseMaterial.Dispose();
            _blueNoiseTexture.Dispose();
            _blueNoiseLayout.Dispose();
            _dataBuffer.Dispose();
            _upsampleDataBuffer?.Dispose();
            _rsmFallbackDepth?.Dispose();
            _gpuTimestamps?.Dispose();
            _staticBvh.Dispose();
            _dynamicBvh.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
