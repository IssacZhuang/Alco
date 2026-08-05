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
    /// <summary>Gets the last sampled GPU duration, or NaN when unavailable.</summary>
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
/// Voxel global illumination renderer for the deferred PBR pipeline: a cascaded
/// voxel clipmap (4 levels, each a cube of <c>resolution</c>^3 voxels at twice
/// the previous level's voxel size, following the camera) with compute
/// voxelization, direct-light injection, deterministic rotation-balanced
/// diffuse cone tracing and hybrid screen-space/voxel-cone reflections.
/// <br/>Mesh geometry is registered once through <see cref="RegisterMesh"/> and
/// shared by persistent structural instances and per-frame movable instances.
/// Structural bricks are rebuilt incrementally after edits or camera scrolling.
/// <br/>Call <see cref="Render"/> after the G-buffer pass and before the lighting
/// pass; the resulting configurable-resolution <see cref="IndirectTexture"/>
/// atlas (diffuse near/far layers with layer depths, then specular) is sampled
/// by the deferred lighting pass, which blends the layers at full-resolution
/// depth (see <see cref="PBRDeferredPipeline.SetGlobalIllumination"/>).
/// <br/>Attribute voxels live in storage buffers (packed, point-sampled by the
/// injection pass); the HDR radiance volume is one mip-mapped RGBA16Float
/// <see cref="Texture3D"/> with all clipmap levels stacked along its depth axis,
/// cone-traced with hardware trilinear filtering.
/// </summary>
public sealed class VoxelGiRenderer : AutoDisposable
{
    /// <summary>
    /// Per-frame data uploaded to every voxel GI shader. Layout must match the
    /// <c>_data</c> cbuffer in VoxelCommon.hlsli exactly. The caller fills the
    /// lighting fields; the renderer fills the clipmap fields (level origins,
    /// <see cref="ClipmapParams"/>) and the trace/G-buffer resolution components
    /// of <see cref="GiParams"/> / <see cref="GiParams2"/>.
    /// </summary>
    public struct VoxelGiData
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
        /// <summary>x=level resolution y=level count z=mip count (filled by the renderer).</summary>
        public Vector4 ClipmapParams;
        /// <summary>x=shadowEnabled y=numPointLights z=shadowMapSize w=unused.</summary>
        public Vector4 LightingParams;
        /// <summary>x=emissiveScale y=traceMaxDistance zw=trace resolution in pixels (filled by the renderer).</summary>
        public Vector4 GiParams;
        /// <summary>x=debugView yz=G-buffer resolution in pixels (filled by the renderer) w=giSkyIntensity (sky light multiplier for voxel GI).</summary>
        public Vector4 GiParams2;
        /// <summary>x=frame index, y=GI diffuse bias, z=history-valid flag (filled by the renderer), w=diffuse spreading (dual-kernel opacity bias).</summary>
        public Vector4 GiFrameParams;
    }

    /// <summary>
    /// Push constant payload for one voxelize dispatch. Layout must match the
    /// <c>VoxelizeConstants</c> struct in Voxelize.hlsl exactly (128 bytes, the
    /// device push-constant limit).
    /// </summary>
    private struct VoxelizeConstants
    {
        public Matrix4x4 Model;
        public Vector4 BaseColor;
        public Vector4 Emissive;
        public Vector4 Params;  // x=levelIndex, y=indexIs16Bit, z=vertexStrideUints, w=alphaCutoff
        public Vector4 Params2; // x=triangleCount
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
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly ComputeMaterial _clearMaterial;
    private readonly ComputeMaterial _voxelizeMaterial;
    private readonly ComputeMaterial _injectMaterial;
    private readonly ComputeMaterial _mipMaterial;
    private readonly ComputeMaterial _mipChainMaterial;
    private readonly ComputeMaterial _propagateMaterial;
    private readonly ComputeMaterial _traceMaterial;
    private readonly ComputeMaterial _demosaicMaterial;
    private readonly GraphicsValueBuffer<VoxelGiData> _dataBuffer;
    private readonly GPUTimestampQuerySet? _timestampQueries;
    private readonly GPUBuffer? _timestampResolveBuffer;

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
    private bool _hasPendingTimestamps;

    private readonly Dictionary<(Mesh Mesh, uint VertexStrideBytes), MeshGeometry> _geometryByMesh = new();
    private readonly List<MeshGeometry> _geometries = new();
    private readonly List<MeshRegistration> _meshes = new();
    private readonly List<StaticInstance?> _staticInstances = new();
    private readonly Stack<int> _freeStaticInstanceHandles = new();
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

    private RenderTexture _traceRaw;
    private RenderTexture _indirectAtlas;
    private readonly RenderTexture[] _historyGI = new RenderTexture[2];
    private uint _gbufferWidth;
    private uint _gbufferHeight;
    private float _traceResolutionScale;
    private int _historyReadIndex;
    private bool _historyValid;
    private Matrix4x4 _viewProjectionPrev = Matrix4x4.Identity;
    private RenderTexture? _boundGBuffer;
    private RenderTexture? _boundShadowMap;

    private const int LevelCount = 4;
    private const int BrickSize = 8;

    /// <summary>
    /// Gets or sets the maximum number of structural bricks rebuilt per clipmap level and frame.
    /// High-priority edit bricks are processed before camera-streaming bricks.
    /// Lower values smooth out the voxelization burst when the camera crosses a
    /// brick boundary (all levels get dirty simultaneously); the trade-off is
    /// that newly-exposed geometry takes more frames to fully voxelize.
    /// </summary>
    public int StaticBrickBudgetPerLevel { get; set; } = 128;

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
    /// Gets or sets the diffuse temporal hysteresis (0..1), independently from
    /// specular. The diffuse direction tile uses a four-frame azimuth mirror,
    /// so the history window needs ~5 frames to cover the cycle: 0.8 averages
    /// roughly five frames, yielding an effective base blend rate of ~0.0625
    /// while avoiding the trailing residual of 0.9.
    /// </summary>
    public float DiffuseTemporalHysteresis { get; set; } = 0.8f;

    /// <summary>
    /// Gets or sets the diffuse spreading amount for the dual-kernel opacity
    /// bias. Lowers the elevation of each diffuse cone toward the surface
    /// tangent, gathering more near-field occlusion for stronger contact AO.
    /// Zero disables the effect (radiance kernel only).
    /// </summary>
    public float DiffuseSpreading { get; set; } = 0.0f;

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
    /// Gets or sets the number of frames between blocking GPU timestamp readbacks.
    /// Zero disables timing; timestamp writes remain unavailable on unsupported adapters.
    /// </summary>
    public int GpuTimingSamplePeriod { get; set; } = 60;

    /// <summary>Gets the most recently completed frame's GI diagnostics.</summary>
    public VoxelGiStatistics Statistics { get; private set; }

    /// <summary>
    /// The gathered indirect radiance atlas (five times the configured trace
    /// width: diffuse near/far layers, specular, ALD near/far layers with
    /// their view-linear layer depths in alpha), sampled by the deferred
    /// lighting pass, which blends the layers at full-resolution depth.
    /// </summary>
    public RenderTexture IndirectTexture => _indirectAtlas;

    /// <summary>
    /// Bind a shared point-light StructuredBuffer to the GI inject pass so that
    /// direct point-light radiance is injected into the voxel volume. The buffer
    /// is typically owned by <see cref="PBRDeferredPipeline"/> and obtained via
    /// <see cref="PBRDeferredPipeline.PointLightBuffer"/>.
    /// </summary>
    /// <param name="buffer">The point-light storage buffer to bind.</param>
    public void SetPointLightBuffer(GraphicsBuffer buffer)
    {
        _injectMaterial.SetBuffer(ShaderResourceId.PointLights, buffer);
    }

    /// <summary>
    /// Create the voxel GI renderer with its compute shaders.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    /// <param name="clearShader">The voxel clear shader (VoxelClear.hlsl).</param>
    /// <param name="voxelizeShader">The triangle voxelization shader (Voxelize.hlsl).</param>
    /// <param name="injectShader">The direct light injection shader (VoxelInject.hlsl).</param>
    /// <param name="mipShader">The radiance mip downsample shader (VoxelMip.hlsl).</param>
    /// <param name="propagateShader">The multi-bounce propagation shader (VoxelPropagate.hlsl).</param>
    /// <param name="traceShader">The cone tracing shader (VoxelTrace.hlsl).</param>
    /// <param name="demosaicShader">The temporal demosaic shader (VoxelDemosaic.hlsl).</param>
    /// <param name="width">The initial G-buffer width in pixels.</param>
    /// <param name="height">The initial G-buffer height in pixels.</param>
    /// <param name="resolution">The voxel resolution of each clipmap level (power of two).</param>
    /// <param name="baseVoxelSize">The voxel size of the finest level in world units.</param>
    /// <param name="traceResolutionScale">Screen-space cone-trace resolution relative to the G-buffer (0.25..1.0).</param>
    /// <exception cref="ArgumentException">The voxel resolution or trace-resolution scale is invalid.</exception>
    public VoxelGiRenderer(
        RenderingSystem rendering,
        Shader clearShader,
        Shader voxelizeShader,
        Shader injectShader,
        Shader mipShader,
        Shader mipChainShader,
        Shader propagateShader,
        Shader traceShader,
        Shader demosaicShader,
        uint width,
        uint height,
        int resolution = 128,
        float baseVoxelSize = 0.1f,
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
        _clearMaterial = rendering.CreateComputeMaterial(clearShader);
        _voxelizeMaterial = rendering.CreateComputeMaterial(voxelizeShader);
        _injectMaterial = rendering.CreateComputeMaterial(injectShader);
        _mipMaterial = rendering.CreateComputeMaterial(mipShader);
        _mipChainMaterial = rendering.CreateComputeMaterial(mipChainShader);
        _propagateMaterial = rendering.CreateComputeMaterial(propagateShader);
        _traceMaterial = rendering.CreateComputeMaterial(traceShader);
        _demosaicMaterial = rendering.CreateComputeMaterial(demosaicShader);
        _dataBuffer = rendering.CreateGraphicsValueBuffer<VoxelGiData>("voxel_gi_data");
        if (_device.TimestampQuerySupported)
        {
            _timestampQueries = _device.CreateTimestampQuerySet(2, "voxel_gi_timestamps");
            _timestampResolveBuffer = _device.CreateBuffer(new BufferDescriptor
            {
                Usage = BufferUsage.QueryResolve | BufferUsage.CopySrc,
                Size = sizeof(ulong) * 2,
                Name = "voxel_gi_timestamp_resolve",
            });
        }

        _clearMaterial.SetBuffer("_data", _dataBuffer);
        _voxelizeMaterial.SetBuffer("_data", _dataBuffer);
        _injectMaterial.SetBuffer("_data", _dataBuffer);
        _mipMaterial.SetBuffer("_data", _dataBuffer);
        _mipChainMaterial.SetBuffer("_data", _dataBuffer);
        _propagateMaterial.SetBuffer("_data", _dataBuffer);
        _traceMaterial.SetBuffer("_data", _dataBuffer);
        _demosaicMaterial.SetBuffer("_data", _dataBuffer);

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
        _traceRaw = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 3, traceHeight, "voxel_trace_raw");
        // History: 6 segments (diffuse near/far, specular, ALD near/far,
        // disocclusion metadata). The demosaic shader derives halfWidth as
        // traceRaw.Width / 3 (one segment width).
        _historyGI[0] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_a");
        _historyGI[1] = rendering.CreateRenderTexture(rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_b");
        _traceMaterial.SetRenderTexture("_indirectGI", _traceRaw);
        _demosaicMaterial.SetRenderTexture("_traceInput", _traceRaw, 0);
        _demosaicMaterial.SetRenderTexture("_indirectGI", _indirectAtlas, 0);
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
        RenderTexture? newHistoryA = null;
        RenderTexture? newHistoryB = null;
        try
        {
            newIndirectAtlas = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 5, traceHeight, "voxel_indirect_gi");
            newTraceRaw = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 3, traceHeight, "voxel_trace_raw");
            newHistoryA = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_a");
            newHistoryB = _rendering.CreateRenderTexture(
                _rendering.PreferredLightMapPass, traceWidth * 6, traceHeight, "voxel_history_b");
        }
        catch
        {
            newIndirectAtlas?.Dispose();
            newTraceRaw?.Dispose();
            newHistoryA?.Dispose();
            newHistoryB?.Dispose();
            throw;
        }

        _indirectAtlas.Dispose();
        _traceRaw.Dispose();
        _historyGI[0].Dispose();
        _historyGI[1].Dispose();
        _indirectAtlas = newIndirectAtlas;
        _traceRaw = newTraceRaw;
        _historyGI[0] = newHistoryA;
        _historyGI[1] = newHistoryB;
        _traceMaterial.SetRenderTexture("_indirectGI", _traceRaw);
        _demosaicMaterial.SetRenderTexture("_traceInput", _traceRaw, 0);
        _demosaicMaterial.SetRenderTexture("_indirectGI", _indirectAtlas, 0);
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
    /// <param name="gbuffer">The pipeline G-buffer (depth + world-normal + metallic-roughness-ao attachments).</param>
    /// <param name="shadowMap">The pipeline shadow map (2x2 cascade atlas).</param>
    /// <param name="data">Per-frame data; the clipmap fields are filled by the renderer.</param>
    /// <param name="cameraPosition">The world-space camera position driving the clipmap.</param>
    public void Render(RenderTexture gbuffer, RenderTexture shadowMap, ref VoxelGiData data, in Vector3 cameraPosition)
    {
        long recordStart = Stopwatch.GetTimestamp();
        int staticBricksUpdated = 0;
        int dynamicBricksUpdated = 0;
        int droppedBricks = 0;
        _clipmap.UpdateOrigins(cameraPosition);
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
        data.ClipmapParams = new Vector4(_resolution, LevelCount, _mipCount, 0.0f);
        uint traceWidth = Math.Max(_traceRaw.Width / 3, 1);
        data.GiParams = new Vector4(data.GiParams.X, data.GiParams.Y, traceWidth, _traceRaw.Height);
        data.GiParams2 = new Vector4(data.GiParams2.X, gbuffer.Width, gbuffer.Height, data.GiParams2.W);
        data.GiFrameParams = new Vector4(_frameIndex, 0.05f, _historyValid ? 1.0f : 0.0f, DiffuseSpreading);
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
            _boundGBuffer = gbuffer;
        }
        if (!ReferenceEquals(_boundShadowMap, shadowMap))
        {
            _injectMaterial.SetRenderTextureDepth("_shadowMap", shadowMap);
            _boundShadowMap = shadowMap;
        }

        uint resolution = (uint)_resolution;
        _dynamicPagePool.Reset();
        bool measureGpu = _timestampQueries != null
            && _timestampResolveBuffer != null
            && GpuTimingSamplePeriod > 0
            && _frameIndex % (uint)GpuTimingSamplePeriod == 0;

        // Read back the previous period's GPU timestamps (non-blocking — the
        // work is guaranteed complete since at least GpuTimingSamplePeriod
        // frames have elapsed). This avoids the CPU-GPU synchronization stall
        // that occurred when reading immediately after Submit.
        if (_hasPendingTimestamps)
        {
            var timestamps = new ulong[2];
            _device.ReadBuffer(_timestampResolveBuffer!, timestamps);
            if (timestamps[1] >= timestamps[0])
            {
                _gpuMilliseconds = (timestamps[1] - timestamps[0])
                    * _device.TimestampPeriodNanoseconds
                    / 1_000_000.0;
            }
            _hasPendingTimestamps = false;
        }

        _commandBuffer.Begin();
        using (GPUCommandBuffer.ComputePass computePass = measureGpu
            ? _commandBuffer.BeginCompute(_timestampQueries!, 0, 1)
            : _commandBuffer.BeginCompute())
        {
            // Structural voxelization is driven by high-priority edit bricks and
            // lower-priority camera-streaming bricks. The clipmap buffer is toroidal,
            // so retained bricks survive camera movement without being copied.
            for (int level = 0; level < LevelCount; level++)
            {
                bool fullReset = _staticNeedsFullClear[level] || _clipmap.ConsumeFullReset(level);
                if (fullReset)
                {
                    _staticPagePool.ResetLevel(level);
                    _staticNeedsFullClear[level] = false;
                }

                int maximumBricks = Math.Clamp(
                    StaticBrickBudgetPerLevel,
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
                VoxelGiBounds levelBounds = _clipmap.GetLevelBounds(level);
                for (int i = 0; i < _staticInstances.Count; i++)
                {
                    StaticInstance? instance = _staticInstances[i];
                    if (instance == null
                        || !instance.Active
                        || !instance.WorldBounds.Intersects(dirtyBounds)
                        || !instance.WorldBounds.Intersects(levelBounds))
                    {
                        continue;
                    }
                    DispatchVoxelize(computePass, instance.Registration, _attrStatic, _pageTableStatic[level], level,
                        instance.World, instance.BaseColor, instance.Emissive, instance.AlphaCutoff);
                }
            }

            // Movable geometry is rebuilt in a separate sparse pool each frame
            // and limited to the nearest configured clipmap levels.
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
                }

                VoxelGiBounds levelBounds = _clipmap.GetLevelBounds(level);
                for (int i = 0; i < _instances.Count; i++)
                {
                    DynamicInstance instance = _instances[i];
                    if (!instance.WorldBounds.Intersects(levelBounds))
                    {
                        continue;
                    }
                    DispatchVoxelize(computePass, instance.Registration, _attrDynamic, _pageTableDynamic[level], level,
                        instance.World, instance.BaseColor, instance.Emissive, instance.AlphaCutoff);
                }
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

            // Build mip chain after injection so the propagation cones sample
            // correct coarse-mip data instead of stale values from the previous
            // frame. Without this, the first bounce gathers against an empty or
            // outdated radiance volume.
            int radianceReadIndex = 0;
            BuildMipChains(computePass, _radiance[radianceReadIndex]);

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

            // Gather rotation-balanced narrow-cone diffuse and specular from the
            // last-written radiance texture (direct + bounce).
            _traceMaterial.SetTexture("_radiance", _radiance[radianceReadIndex]);
            _traceMaterial.DispatchBySize(computePass, traceWidth, _traceRaw.Height, 1);

            // Dual-layer diffuse resolve plus specular, one thread per trace
            // pixel writing all three atlas sections, followed by validated
            // per-layer history accumulation.
            int historyRead = _historyReadIndex;
            int historyWrite = 1 - historyRead;
            _demosaicMaterial.SetRenderTexture("_historyInput", _historyGI[historyRead], 0);
            _demosaicMaterial.SetRenderTexture("_historyOut", _historyGI[historyWrite], 0);
            _demosaicMaterial.DispatchBySizeWithConstant(
                computePass,
                traceWidth,
                _traceRaw.Height,
                1,
                new Vector4(TemporalHysteresis, 1.0f, DiffuseTemporalHysteresis, 0.0f));
        }
        if (measureGpu)
        {
            _commandBuffer.ResolveTimestamps(_timestampQueries!, 0, 2, _timestampResolveBuffer!);
            _hasPendingTimestamps = true;
        }
        _commandBuffer.End();
        _device.Submit(_commandBuffer);

        _instances.Clear();
        _viewProjectionPrev = data.ViewProjection;
        _historyReadIndex = 1 - _historyReadIndex;
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
        for (int i = 0; i < _staticInstances.Count; i++)
        {
            StaticInstance? instance = _staticInstances[i];
            if (instance != null && instance.Active && instance.WorldBounds.Intersects(bounds))
            {
                return true;
            }
        }
        return false;
    }

    private void CollectDynamicBricks(int level)
    {
        _dirtyBricks.Clear();
        _brickKeys.Clear();
        VoxelGiBounds levelBounds = _clipmap.GetLevelBounds(level);
        for (int instanceIndex = 0; instanceIndex < _instances.Count; instanceIndex++)
        {
            DynamicInstance instance = _instances[instanceIndex];
            if (!instance.WorldBounds.Intersects(levelBounds))
            {
                continue;
            }

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
        float alphaCutoff)
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
            Params2 = new Vector4(geometry.TriangleCount, 0.0f, 0.0f, 0.0f),
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
            _indirectAtlas.Dispose();
            _historyGI[0].Dispose();
            _historyGI[1].Dispose();
            _dataBuffer.Dispose();
            _timestampQueries?.Dispose();
            _timestampResolveBuffer?.Dispose();
            _commandBuffer.Dispose();
        }
    }
}
