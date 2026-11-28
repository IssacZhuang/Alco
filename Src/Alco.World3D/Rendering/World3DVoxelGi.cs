using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const int VoxelMaxCandidates = 16384;
    private const int VoxelMaxChunks = 131072;
    private const int VoxelPoolStateUints = 12;
    private const ulong VoxelCountersBytes = 40;
    private const uint VoxelIndirectStride = 16;
    private const int VoxelMaxEvictBoxes = 128;
    private const string StandardVoxelSurfaceModule = "World3DPbrStandard";

    private readonly World3DVoxelClipmap _voxelClipmap = new();
    private readonly List<string> _surfaceBucketModules = [StandardVoxelSurfaceModule];
    private readonly Dictionary<string, uint> _surfaceBucketIds = new(StringComparer.Ordinal)
    {
        [StandardVoxelSurfaceModule] = 0u,
    };
    private readonly List<StaticDirtyBounds> _dirtyVoxelStaticBounds = [];
    private readonly List<MipDispatchDesc> _voxelMipDispatches = [];
    private readonly List<uint> _activeSurfaceBuckets = [];
    private readonly Dictionary<string, Shader> _voxelSurfaceShaders = new(StringComparer.Ordinal);
    private readonly Texture?[] _voxelVolumes = new Texture?[World3DVoxelClipmap.CascadeCount];
    private readonly Texture?[] _giHistoryDiffuse = new Texture?[2];
    private readonly Texture?[] _giHistorySpecular = new Texture?[2];

    private Texture? _voxelBlueNoise;
    private Texture? _giRawDiffuse;
    private Texture? _giRawSpecular;
    private Texture? _giResolvedDiffuse;
    private Texture? _giResolvedSpecular;
    private Texture? _giFullDiffuse;
    private Texture? _giFullSpecular;

    private GpuBuffer? _voxelDummyBuffer;
    private GpuBuffer? _voxelPoolState;
    private GpuBuffer? _voxelCounters;
    private GpuBuffer? _voxelStaticFreeList;
    private GpuBuffer? _voxelStaticPageTable;
    private GpuBuffer? _voxelStaticReverseTable;
    private GpuBuffer? _voxelStaticDirtyMask;
    private GpuBuffer? _voxelStaticDirtyQueue;
    private GpuBuffer? _voxelStaticActiveDirtyList;
    private GpuBuffer? _voxelStaticBrickPool;
    private GpuBuffer? _voxelStaticPropBuffer;
    private GpuBuffer? _voxelDynamicDesiredMask;
    private GpuBuffer? _voxelDynamicPageTable;
    private GpuBuffer? _voxelDynamicReverseTable;
    private GpuBuffer? _voxelDynamicActiveList;
    private GpuBuffer? _voxelDynamicBrickPool;
    private GpuBuffer? _voxelDynamicPropBuffer;
    private GpuBuffer? _voxelCandidateList;
    private GpuBuffer? _voxelChunkList;
    private GpuBuffer? _voxelIndirectArgs;

    private bool _voxelShadersLoaded;
    private Shader? _voxelInitShader;
    private Shader? _voxelBlueNoiseShader;
    private Shader? _voxelPageManageShader;
    private Shader? _voxelCullInstancesShader;
    private Shader? _voxelPrepareArgsShader;
    private Shader? _voxelExpandChunksShader;
    private Shader? _voxelClearShader;
    private Shader? _voxelInjectShader;
    private Shader? _voxelMipShader;
    private Shader? _voxelMipChainShader;
    private Shader? _voxelPropagateShader;
    private Shader? _voxelTraceShader;
    private Shader? _voxelDemosaicShader;
    private Shader? _voxelUpsampleShader;

    private int _voxelAllocResolution;
    private int _voxelAllocStaticCapacity;
    private int _voxelAllocDynamicCapacity;
    private int _voxelAllocDynamicLevels;
    private uint _voxelVolumeMipCount;
    private bool _voxelNeedsInit = true;
    private bool _voxelBlueNoiseDirty = true;
    private bool _voxelGiForceReset = true;
    private int _voxelBootstrapFramesRemaining;
    private int _pendingVoxelBootstrapFrames;
    private ulong _voxelFrameCounter;
    private int _giHistoryWriteIndex;
    private int _giSubmittedHistoryIndex = -1;
    private bool _giHistoryValid;
    private Extent2D _giScreenViewport;
    private bool _giScreenHalfRes;
    private World3DView _giPrevView;
    private World3DVoxelGiGpuCounters _lastVoxelReadbackCounters;

    private bool _recordedVoxelGiEnabled;
    private bool _recordedVoxelHalfRes;
    private bool _recordedVoxelDynamicActive;
    private bool _recordedVoxelHasRsm;
    private float _recordedVoxelTemporalAlpha;
    private int _recordedPointLightCount;
    private BufferSlice _framePointLights;
    private BufferSlice _frameVoxelCascades;
    private BufferSlice _frameVoxelEvictBoxes;
    private BufferSlice _frameVoxelGiViews;
    private BufferSlice _frameVoxelFrame;
    private World3DVoxelClipmap.FramePlan _recordedVoxelPlan;
    private int _recordedVoxelEvictBoxCount;
    private int _recordedVoxelDynamicLevels;
    private Matrix4x4 _recordedRsmInvViewProj;
    private Vector4 _recordedVoxelSkyHorizon;
    private Vector4 _recordedVoxelSkyZenith;
    private Vector4 _recordedVoxelSunColor;
    private Vector4 _recordedVoxelSunDirIntensity;
    private readonly uint[] _recordedVoxelBudgets = new uint[World3DVoxelClipmap.CascadeCount];
    private RGTexture _frameGiRawDiffuseRG;
    private RGTexture _frameGiOutputDiffuseRG;
    private RGTexture _frameGiOutputSpecularRG;

    /// <summary>Gets the GPU-driven sparse brick clipmap VoxelGI configuration.</summary>
    public World3DVoxelGiSettings VoxelGi { get; } = new();

    /// <summary>Gets the latest recorded VoxelGI status and last readback counters.</summary>
    public World3DVoxelGiStats LastRecordedVoxelGi { get; private set; }

    /// <summary>Requests a full non-blocking clipmap re-voxelization and clears temporal history.</summary>
    public void InvalidateVoxelGi()
    {
        BeginEdit();
        _voxelGiForceReset = true;
        _giHistoryValid = false;
    }

    /// <summary>Synchronously reads the GPU VoxelGI pool, allocation, fallback, and dispatch counters.</summary>
    /// <param name="counters">Receives the counter snapshot when a submitted GPU VoxelGI frame exists.</param>
    /// <returns><see langword="true"/> if counters were read back from GPU memory.</returns>
    public bool TryReadVoxelGiCounters(out World3DVoxelGiGpuCounters counters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        counters = default;
        if (_context == null || _context.IsDisposed || _context.CurrentFrameId != 0 || _system.ActiveBackend == GraphicsBackend.NoGPU)
            return false;
        ReconcileFrame();
        if (!_submittedWorld || _voxelCounters == null)
            return false;

        Span<byte> bytes = stackalloc byte[(int)VoxelCountersBytes];
        _system.ReadBuffer(_voxelCounters, bytes);
        counters = MemoryMarshal.Read<World3DVoxelGiGpuCounters>(bytes);
        _lastVoxelReadbackCounters = counters;
        LastRecordedVoxelGi = LastRecordedVoxelGi with { LastReadbackCounters = counters };
        return true;
    }

    internal BufferSlice GetVoxelDummyView() => default;

    private uint ResolveSurfaceBucket(string? surfaceModule)
    {
        string module = string.IsNullOrEmpty(surfaceModule) ? StandardVoxelSurfaceModule : surfaceModule;
        if (_surfaceBucketIds.TryGetValue(module, out uint bucket))
            return bucket;
        if (_surfaceBucketModules.Count >= 256)
            throw new InvalidOperationException("World3D scenes support at most 256 distinct ISurface modules.");
        bucket = (uint)_surfaceBucketModules.Count;
        _surfaceBucketModules.Add(module);
        _surfaceBucketIds.Add(module, bucket);
        return bucket;
    }

    private void MarkVoxelGiSpatialDirty(World3DInstanceSpatialGpu spatial)
    {
        if (!_voxelClipmap.TryGetCoarsestCommittedWorldBounds(out World3DPosition clipMin, out World3DPosition clipMax))
            return;

        double cellBaseX = (double)spatial.Cell.X * World3DSceneCoordinates.CellSize;
        double cellBaseY = (double)spatial.Cell.Y * World3DSceneCoordinates.CellSize;
        double cellBaseZ = (double)spatial.Cell.Z * World3DSceneCoordinates.CellSize;
        double cx = cellBaseX + spatial.ObjectToCell.M41 + spatial.BoundsCenterRadius.X;
        double cy = cellBaseY + spatial.ObjectToCell.M42 + spatial.BoundsCenterRadius.Y;
        double cz = cellBaseZ + spatial.ObjectToCell.M43 + spatial.BoundsCenterRadius.Z;
        double ex = Math.Max(0.0, spatial.BoundsExtent.X) + 0.05;
        double ey = Math.Max(0.0, spatial.BoundsExtent.Y) + 0.05;
        double ez = Math.Max(0.0, spatial.BoundsExtent.Z) + 0.05;

        double minX = cx - ex;
        double minY = cy - ey;
        double minZ = cz - ez;
        double maxX = cx + ex;
        double maxY = cy + ey;
        double maxZ = cz + ez;
        if (maxX < clipMin.X || minX > clipMax.X ||
            maxY < clipMin.Y || minY > clipMax.Y ||
            maxZ < clipMin.Z || minZ > clipMax.Z)
            return;

        World3DPosition dirtyMin = new(Math.Max(minX, clipMin.X), Math.Max(minY, clipMin.Y), Math.Max(minZ, clipMin.Z));
        World3DPosition dirtyMax = new(Math.Min(maxX, clipMax.X), Math.Min(maxY, clipMax.Y), Math.Min(maxZ, clipMax.Z));
        if (_dirtyVoxelStaticBounds.Count < 48)
        {
            _dirtyVoxelStaticBounds.Add(new StaticDirtyBounds(dirtyMin, dirtyMax));
        }
        else
        {
            StaticDirtyBounds last = _dirtyVoxelStaticBounds[^1];
            _dirtyVoxelStaticBounds[^1] = new StaticDirtyBounds(
                new World3DPosition(
                    Math.Min(last.MinWorld.X, dirtyMin.X),
                    Math.Min(last.MinWorld.Y, dirtyMin.Y),
                    Math.Min(last.MinWorld.Z, dirtyMin.Z)),
                new World3DPosition(
                    Math.Max(last.MaxWorld.X, dirtyMax.X),
                    Math.Max(last.MaxWorld.Y, dirtyMax.Y),
                    Math.Max(last.MaxWorld.Z, dirtyMax.Z)));
        }
    }

    private void MarkVoxelGiMaterialDirty(MaterialEntry material)
    {
        foreach (int slot in material.Instances)
        {
            InstanceEntry inst = _instances.GetLive(slot);
            if (!inst.Description.Dynamic && inst.Description.AffectsGlobalIllumination
                && inst.Material.Route.AlphaMode != World3DAlphaMode.Blend)
                MarkVoxelGiSpatialDirty(inst.Spatial);
        }
    }

    private void MarkVoxelGiGeometryDirty(GeometryEntry geometry)
    {
        foreach (int slot in geometry.Instances)
        {
            InstanceEntry inst = _instances.GetLive(slot);
            if (!inst.Description.Dynamic && inst.Description.AffectsGlobalIllumination
                && inst.Material.Route.AlphaMode != World3DAlphaMode.Blend)
                MarkVoxelGiSpatialDirty(inst.Spatial);
        }
    }

    private void PrepareVoxelGi(RenderContext context, in World3DCamera camera, Extent2D viewport,
        in World3DView primaryView, float deltaTime)
    {
        _recordedVoxelGiEnabled = false;
        _frameGiRawDiffuseRG = default;
        _frameGiOutputDiffuseRG = default;
        _frameGiOutputSpecularRG = default;
        _framePointLights = default;
        _recordedPointLightCount = 0;
        _voxelMipDispatches.Clear();

        if (!_recordedWorld)
        {
            LastRecordedVoxelGi = new World3DVoxelGiStats(false, 0, 0, 0f, 0, 0, 0, 0, 0, 0, _lastVoxelReadbackCounters);
            return;
        }

        if (!VoxelGi.Enabled || viewport.X == 0 || viewport.Y == 0)
        {
            _giHistoryValid = false;
            LastRecordedVoxelGi = new World3DVoxelGiStats(
                false,
                VoxelGi.Resolution,
                World3DVoxelClipmap.CascadeCount,
                VoxelGi.BaseVoxelSize,
                VoxelGi.StaticBrickPoolCapacity,
                VoxelGi.DynamicBrickPoolCapacity,
                0,
                0,
                0,
                _recordedPointLightCount,
                _lastVoxelReadbackCounters);
            return;
        }

        ++_voxelFrameCounter;
        EnsureVoxelGiShaders();

        int res = World3DVoxelClipmap.NormalizeResolution(VoxelGi.Resolution);
        int staticCap = Math.Clamp(VoxelGi.StaticBrickPoolCapacity, 256, 32768);
        int dynLevels = Math.Clamp(VoxelGi.DynamicLevelCount, 0, World3DVoxelClipmap.CascadeCount);
        int dynCap = dynLevels > 0 ? Math.Clamp(VoxelGi.DynamicBrickPoolCapacity, 0, 16384) : 0;
        bool halfRes = VoxelGi.HalfResolutionTrace;

        EnsureVoxelGiVolumeAndPoolResources(res, staticCap, dynCap, dynLevels);
        EnsureVoxelGiScreenTargets(viewport, halfRes);

        _recordedVoxelGiEnabled = true;
        _recordedVoxelHalfRes = halfRes;
        _recordedVoxelDynamicActive = dynCap > 0 && dynLevels > 0;
        _recordedVoxelDynamicLevels = dynLevels;

        bool forceReset = _voxelGiForceReset || _voxelNeedsInit;
        _voxelClipmap.Plan(camera.Eye, primaryView.OriginAnchorCell, res, VoxelGi.BaseVoxelSize, forceReset, out _recordedVoxelPlan);
        PrepareVoxelPointLights(context, in primaryView, in _recordedVoxelPlan);

        Span<World3DVoxelClipmap.EvictBoxGpu> combinedBoxes = stackalloc World3DVoxelClipmap.EvictBoxGpu[VoxelMaxEvictBoxes];
        int evictCount = 0;
        for (int i = 0; i < _recordedVoxelPlan.EvictBoxCount && evictCount < VoxelMaxEvictBoxes; ++i)
            combinedBoxes[evictCount++] = _recordedVoxelPlan.EvictBoxes[i];

        if (_dirtyVoxelStaticBounds.Count > 0 && _recordedVoxelPlan.ResetCascadeMask != 0b1111)
            AppendDirtyBoundsEvictBoxes(in _recordedVoxelPlan, combinedBoxes, ref evictCount);

        _recordedVoxelEvictBoxCount = evictCount;
        _frameVoxelCascades = context.UploadTransient<World3DVoxelClipmap.CascadeGpu>(_recordedVoxelPlan.Cascades);
        _frameVoxelEvictBoxes = evictCount > 0
            ? context.UploadTransient<World3DVoxelClipmap.EvictBoxGpu>(combinedBoxes[..evictCount])
            : _voxelDummyBuffer!.Slice(0, 32);

        int bpa = res / World3DVoxelClipmap.BrickSize;
        int perCascade = bpa * bpa * bpa;
        _pendingVoxelBootstrapFrames = !VoxelGi.BootstrapFullVolumeOnReset
            ? 0
            : (_recordedVoxelPlan.ResetCascadeMask != 0
                ? Math.Max(2, (perCascade * World3DVoxelClipmap.CascadeCount + staticCap - 1) / staticCap + 1)
                : Math.Max(0, _voxelBootstrapFramesRemaining - 1));
        ComputePerCascadeBudgets(bpa, staticCap, _recordedVoxelPlan.ResetCascadeMask != 0 || _pendingVoxelBootstrapFrames > 0);

        bool canHistory = _giHistoryValid
            && _giSubmittedHistoryIndex >= 0
            && _giScreenViewport.Equals(viewport)
            && _giScreenHalfRes == halfRes;
        _giHistoryWriteIndex = canHistory ? 1 - _giSubmittedHistoryIndex : 0;

        float dt = Math.Clamp(deltaTime, 0f, 0.25f);
        float blendSpeed = Math.Clamp(VoxelGi.TemporalBlendSpeed, 0.25f, 60f);
        _recordedVoxelTemporalAlpha = canHistory ? Math.Clamp(1f - MathF.Exp(-blendSpeed * dt), 0.02f, 1f) : 1f;

        Span<World3DView> viewsPair = stackalloc World3DView[2];
        viewsPair[0] = primaryView;
        viewsPair[1] = canHistory ? _giPrevView : primaryView;
        _frameVoxelGiViews = context.UploadTransient<World3DView>(viewsPair);

        _recordedVoxelHasRsm = _recordedShadowsEnabled
            && _recordedRsmEnabled
            && _pendingRsmState.Valid
            && _rsmDepth != null
            && Matrix4x4.Invert(_pendingRsmState.GpuCascade.LightViewProj, out _recordedRsmInvViewProj);
        if (!_recordedVoxelHasRsm)
            _recordedRsmInvViewProj = Matrix4x4.Identity;

        EvaluateLightingForVoxelGi(Lighting, out _recordedVoxelSunDirIntensity, out _recordedVoxelSunColor,
            out _recordedVoxelSkyHorizon, out _recordedVoxelSkyZenith);

        PopulateActiveSurfaceBuckets();
        BuildMipDispatchList((uint)res);

        uint bpaU = (uint)_recordedVoxelPlan.BricksPerAxis;
        uint perCascadeU = bpaU * bpaU * bpaU;
        uint totalStaticSlots = World3DVoxelClipmap.CascadeCount * perCascadeU;
        uint totalDynSlots = (uint)_recordedVoxelDynamicLevels * perCascadeU;
        uint staticMaskWords = (totalStaticSlots + 31u) / 32u;
        uint dynMaskWords = _recordedVoxelDynamicActive ? (totalDynSlots + 31u) / 32u : 0u;
        bool hasDyn = _recordedVoxelDynamicActive && _voxelDynamicBrickPool != null;
        GPUBufferView dummy = _voxelDummyBuffer!.View;
        Extent3D traceExt = _giRawDiffuse!.Extent;

        VoxelFrameGpu vFrame = new(
            _recordedVoxelHasRsm ? _pendingRsmState.GpuCascade : default,
            _recordedRsmInvViewProj,
            _recordedVoxelSunColor,
            _recordedVoxelSunDirIntensity,
            _recordedVoxelSkyHorizon,
            new Vector4(_recordedVoxelSkyZenith.X, _recordedVoxelSkyZenith.Y, _recordedVoxelSkyZenith.Z, (float)(_voxelFrameCounter & 1023u)),
            _recordedVoxelBudgets[0],
            _recordedVoxelBudgets[1],
            _recordedVoxelBudgets[2],
            _recordedVoxelBudgets[3],
            _frameVoxelCascades.View,
            _frameVoxelEvictBoxes.View,
            _voxelPoolState!.View,
            _voxelStaticFreeList!.View,
            _voxelStaticPageTable!.View,
            _voxelStaticReverseTable!.View,
            _voxelStaticDirtyMask!.View,
            _voxelStaticDirtyQueue!.View,
            _voxelStaticActiveDirtyList!.View,
            hasDyn ? _voxelDynamicDesiredMask!.View : dummy,
            hasDyn ? _voxelDynamicPageTable!.View : dummy,
            hasDyn ? _voxelDynamicReverseTable!.View : dummy,
            hasDyn ? _voxelDynamicActiveList!.View : dummy,
            _voxelStaticBrickPool!.View,
            hasDyn ? _voxelDynamicBrickPool!.View : dummy,
            _voxelCounters!.View,
            _framePointLights.IsEmpty ? dummy : _framePointLights.View,
            dummy,
            GPUTextureId.FromSampled(_voxelBlueNoise!),
            _recordedVoxelHasRsm && _rsmDepth != null ? GPUTextureId.FromSampled(_rsmDepth) : GPUTextureId.Invalid,
            GPUTextureId.FromSampled(_voxelVolumes[0]!),
            GPUTextureId.FromSampled(_voxelVolumes[1]!),
            GPUTextureId.FromSampled(_voxelVolumes[2]!),
            GPUTextureId.FromSampled(_voxelVolumes[3]!),
            bpaU,
            (uint)staticCap,
            (uint)dynCap,
            (uint)dynLevels,
            (uint)_recordedVoxelEvictBoxCount,
            staticMaskWords,
            dynMaskWords,
            totalStaticSlots,
            totalDynSlots,
            (uint)_recordedPointLightCount,
            _recordedVoxelHasRsm ? _pendingRsmState.DepthRange : 1f,
            MathF.Max(0f, VoxelGi.EmissiveBoost),
            MathF.Max(0f, VoxelGi.IndirectDiffuseIntensity),
            MathF.Max(0f, VoxelGi.IndirectSpecularIntensity),
            _recordedVoxelHasRsm ? 1u : 0u,
            halfRes ? 1u : 0u,
            traceExt.X,
            traceExt.Y,
            viewport.X,
            viewport.Y,
            0u,
            0u);
        _frameVoxelFrame = context.UploadTransient(in vFrame);

        LastRecordedVoxelGi = new World3DVoxelGiStats(
            true,
            res,
            World3DVoxelClipmap.CascadeCount,
            VoxelGi.BaseVoxelSize,
            staticCap,
            dynCap,
            _recordedVoxelPlan.EvictedBrickCount,
            _recordedVoxelPlan.ResetCascadeMask,
            _recordedVoxelPlan.ShiftedCascadeMask,
            _recordedPointLightCount,
            _lastVoxelReadbackCounters);
    }

    private void ReconcileVoxelGi(bool submitted)
    {
        if (submitted && _recordedWorld && _recordedVoxelGiEnabled)
        {
            _voxelClipmap.Commit();
            _voxelNeedsInit = false;
            _voxelBlueNoiseDirty = false;
            _voxelGiForceReset = false;
            _voxelBootstrapFramesRemaining = _pendingVoxelBootstrapFrames;
            _dirtyVoxelStaticBounds.Clear();
            _giSubmittedHistoryIndex = _giHistoryWriteIndex;
            _giPrevView = _recordedView;
            _giHistoryValid = true;
        }
        else if (!submitted)
        {
            _voxelClipmap.Rollback();
            _giHistoryValid = false;
        }
    }

    private void AppendDirtyBoundsEvictBoxes(
        in World3DVoxelClipmap.FramePlan plan,
        Span<World3DVoxelClipmap.EvictBoxGpu> dst,
        ref int count)
    {
        int bpa = plan.BricksPerAxis;
        for (int c = 0; c < World3DVoxelClipmap.CascadeCount && count < VoxelMaxEvictBoxes; ++c)
        {
            if ((plan.ResetCascadeMask & (1 << c)) != 0)
                continue;
            if (!_voxelClipmap.TryGetCommittedOriginBrick(c, out long origX, out long origY, out long origZ))
                continue;

            double brickMeters = (double)plan.BaseVoxelSize * (1 << c) * World3DVoxelClipmap.BrickSize;
            double invBrick = 1.0 / brickMeters;
            long curOrigX = origX + (long)GetDeltaShift(plan.Cascades[c].ToroidalOriginX, origX, bpa);
            long curOrigY = origY + (long)GetDeltaShift(plan.Cascades[c].ToroidalOriginY, origY, bpa);
            long curOrigZ = origZ + (long)GetDeltaShift(plan.Cascades[c].ToroidalOriginZ, origZ, bpa);

            foreach (StaticDirtyBounds bounds in _dirtyVoxelStaticBounds)
            {
                if (count >= VoxelMaxEvictBoxes)
                    break;
                int minX = (int)Math.Floor(bounds.MinWorld.X * invBrick - curOrigX);
                int minY = (int)Math.Floor(bounds.MinWorld.Y * invBrick - curOrigY);
                int minZ = (int)Math.Floor(bounds.MinWorld.Z * invBrick - curOrigZ);
                int maxX = (int)Math.Floor(bounds.MaxWorld.X * invBrick - curOrigX) + 1;
                int maxY = (int)Math.Floor(bounds.MaxWorld.Y * invBrick - curOrigY) + 1;
                int maxZ = (int)Math.Floor(bounds.MaxWorld.Z * invBrick - curOrigZ) + 1;
                if (maxX <= 0 || maxY <= 0 || maxZ <= 0 || minX >= bpa || minY >= bpa || minZ >= bpa)
                    continue;

                dst[count++] = new World3DVoxelClipmap.EvictBoxGpu(
                    Math.Clamp(minX, 0, bpa - 1),
                    Math.Clamp(minY, 0, bpa - 1),
                    Math.Clamp(minZ, 0, bpa - 1),
                    c,
                    Math.Clamp(maxX, 1, bpa),
                    Math.Clamp(maxY, 1, bpa),
                    Math.Clamp(maxZ, 1, bpa),
                    0);
            }
        }
    }

    private static int GetDeltaShift(uint targetToroidal, long committedOrigin, int bpa)
    {
        int committedTor = (int)((committedOrigin % bpa + bpa) % bpa);
        int delta = (int)targetToroidal - committedTor;
        int half = bpa / 2;
        if (delta > half) delta -= bpa;
        else if (delta < -half) delta += bpa;
        return delta;
    }

    private void ComputePerCascadeBudgets(int bpa, int staticCap, bool bootstrapActive)
    {
        int perCascade = bpa * bpa * bpa;
        if (VoxelGi.BootstrapFullVolumeOnReset && bootstrapActive)
        {
            uint balancedTake = (uint)Math.Min(perCascade, Math.Max(256, staticCap / World3DVoxelClipmap.CascadeCount));
            for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                _recordedVoxelBudgets[c] = balancedTake;
            return;
        }

        int[] src = VoxelGi.StaticBrickBudgets;
        for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
        {
            int val = src != null && c < src.Length ? src[c] : 256;
            _recordedVoxelBudgets[c] = (uint)Math.Clamp(val, 1, Math.Min(perCascade, staticCap));
        }
    }

    private void PopulateActiveSurfaceBuckets()
    {
        _activeSurfaceBuckets.Clear();
        Span<bool> seen = stackalloc bool[256];
        foreach (MaterialEntry entry in _materials.LiveValues())
        {
            if (entry.Instances.Count == 0)
                continue;
            uint bucket = ResolveSurfaceBucket(entry.Route.SurfaceModule);
            if (!seen[(int)bucket])
            {
                seen[(int)bucket] = true;
                _activeSurfaceBuckets.Add(bucket);
            }
        }
        if (_activeSurfaceBuckets.Count == 0)
            _activeSurfaceBuckets.Add(0u);
    }

    private void BuildMipDispatchList(uint resolution)
    {
        uint curMip = 0;
        while (curMip + 3 < _voxelVolumeMipCount)
        {
            uint size1 = Math.Max(1u, resolution >> (int)(curMip + 1));
            uint size2 = Math.Max(1u, resolution >> (int)(curMip + 2));
            uint size3 = Math.Max(1u, resolution >> (int)(curMip + 3));
            if (size1 < 8u)
                break;
            _voxelMipDispatches.Add(new MipDispatchDesc(true, curMip, size1, size2, size3));
            curMip += 3;
        }
        while (curMip + 1 < _voxelVolumeMipCount)
        {
            uint dstSize = Math.Max(1u, resolution >> (int)(curMip + 1));
            _voxelMipDispatches.Add(new MipDispatchDesc(false, curMip, dstSize, 0u, 0u));
            ++curMip;
        }
    }

    private static void EvaluateLightingForVoxelGi(
        in World3DLighting lighting,
        out Vector4 sunDirIntensity,
        out Vector4 sunColor,
        out Vector4 skyHorizonAndAmbientFloor,
        out Vector4 skyZenith)
    {
        EvaluateFilteredSkyAndSun(in lighting, out Vector3 dir, out Vector3 col, out float inten, out Vector3 horizon, out Vector3 zenith);
        sunDirIntensity = new Vector4(dir, inten);
        sunColor = new Vector4(col, 0f);
        skyHorizonAndAmbientFloor = new Vector4(horizon, lighting.AmbientFloor);
        skyZenith = new Vector4(zenith, 0f);
    }

    private void EnsureVoxelGiShaders()
    {
        if (_voxelShadersLoaded)
            return;

        ShaderSystem shaders = _system.Shaders;
        ShaderCompiler compiler = shaders.Compiler;
        compiler.RegisterVirtualModule("World3DVoxelCommon", "World3DVoxelCommon.slang", ReadWorldShader("World3DVoxelCommon"));
        compiler.RegisterVirtualModule("World3DVoxelVoxelize", "World3DVoxelVoxelize.slang", ReadWorldShader("World3DVoxelVoxelize"));

        IReadOnlyList<ShaderTypeLayout> commonTypes = compiler.LoadModuleFromSource(
            "World3DVoxelCommon", "World3DVoxelCommon.slang", ReadWorldShader("World3DVoxelCommon")).GetTypes();
        GpuStore.ValidateLayout<World3DVoxelClipmap.CascadeGpu>(commonTypes.First(t => t.TypeName == "World3DVoxelCascadeGpu"));
        GpuStore.ValidateLayout<World3DVoxelClipmap.EvictBoxGpu>(commonTypes.First(t => t.TypeName == "World3DVoxelEvictBoxGpu"));
        GpuStore.ValidateLayout<VoxelCandidateGpu>(commonTypes.First(t => t.TypeName == "World3DVoxelCandidateGpu"));
        GpuStore.ValidateLayout<VoxelChunkGpu>(commonTypes.First(t => t.TypeName == "World3DVoxelChunkGpu"));
        GpuStore.ValidateLayout<World3DVoxelGiGpuCounters>(commonTypes.First(t => t.TypeName == "World3DVoxelCountersGpu"));
        GpuStore.ValidateLayout<VoxelFrameGpu>(commonTypes.First(t => t.TypeName == "World3DVoxelFrameGpu"));

        _voxelInitShader = LoadSceneCompute<VoxelInitRoot>("World3DVoxelInit");
        _voxelBlueNoiseShader = LoadSceneCompute<VoxelBlueNoiseRoot>("World3DVoxelBlueNoise");
        _voxelPageManageShader = LoadSceneCompute<VoxelPageManageRoot>("World3DVoxelPageManage");
        _voxelCullInstancesShader = LoadSceneCompute<VoxelCullInstancesRoot>("World3DVoxelCullInstances");
        _voxelPrepareArgsShader = LoadSceneCompute<VoxelPrepareArgsRoot>("World3DVoxelPrepareArgs");
        _voxelExpandChunksShader = LoadSceneCompute<VoxelExpandChunksRoot>("World3DVoxelExpandChunks");
        _voxelClearShader = LoadSceneCompute<VoxelClearRoot>("World3DVoxelClear");
        _voxelInjectShader = LoadSceneCompute<VoxelInjectRoot>("World3DVoxelInject");
        _voxelMipShader = LoadSceneCompute<VoxelMipRoot>("World3DVoxelMip");
        _voxelMipChainShader = LoadSceneCompute<VoxelMipChainRoot>("World3DVoxelMipChain");
        _voxelPropagateShader = LoadSceneCompute<VoxelPropagateRoot>("World3DVoxelPropagate");
        _voxelTraceShader = LoadSceneCompute<VoxelTraceRoot>("World3DVoxelTrace");
        _voxelDemosaicShader = LoadSceneCompute<VoxelDemosaicRoot>("World3DVoxelDemosaic");
        _voxelUpsampleShader = LoadSceneCompute<VoxelUpsampleRoot>("World3DVoxelUpsample");
        _voxelShadersLoaded = true;
    }

    private Shader GetVoxelSurfaceShader(string surfaceModule)
    {
        if (_voxelSurfaceShaders.TryGetValue(surfaceModule, out Shader? existing))
            return existing;
        Shader shader = _system.Shaders.Compose("World3DVoxelVoxelize", surfaceModule, ShaderKind.Compute);
        ValidateRoot<VoxelVoxelizeRoot>(shader.Reflection.Root
            ?? throw new InvalidOperationException($"{shader.Name} did not reflect a root layout."));
        _voxelSurfaceShaders.Add(surfaceModule, shader);
        return shader;
    }

    private void EnsureVoxelGiVolumeAndPoolResources(int res, int staticCap, int dynCap, int dynLevels)
    {
        EnsureExactBuffer(ref _voxelDummyBuffer, 64);
        EnsureExactBuffer(ref _voxelPoolState, (ulong)(VoxelPoolStateUints * sizeof(uint)));
        EnsureExactBuffer(ref _voxelCounters, VoxelCountersBytes);
        EnsureExactBuffer(ref _voxelCandidateList, (ulong)(VoxelMaxCandidates * Unsafe.SizeOf<VoxelCandidateGpu>()));
        EnsureExactBuffer(ref _voxelChunkList, (ulong)(VoxelMaxChunks * Unsafe.SizeOf<VoxelChunkGpu>()));
        EnsureExactBuffer(ref _voxelIndirectArgs, 6 * VoxelIndirectStride);

        if (_voxelBlueNoise == null)
        {
            _voxelBlueNoise = _system.CreateTexture(new TextureDesc
            {
                Type = TextureType.TwoD,
                Extent = new Extent3D(64, 64, 1),
                MipLevels = 1,
                LayerCount = 1,
                Format = Format.Rgba16Float,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            });
            _voxelBlueNoiseDirty = true;
        }

        bool staticChanged = _voxelAllocResolution != res || _voxelAllocStaticCapacity != staticCap;
        if (staticChanged)
        {
            int bpa = res / World3DVoxelClipmap.BrickSize;
            int totalSlots = World3DVoxelClipmap.CascadeCount * bpa * bpa * bpa;
            int maskWords = (totalSlots + 31) / 32;

            ReplaceBuffer(ref _voxelStaticFreeList, (ulong)(staticCap * sizeof(uint)));
            ReplaceBuffer(ref _voxelStaticPageTable, (ulong)(totalSlots * sizeof(uint)));
            ReplaceBuffer(ref _voxelStaticReverseTable, (ulong)(staticCap * sizeof(uint)));
            ReplaceBuffer(ref _voxelStaticDirtyMask, (ulong)(maskWords * sizeof(uint)));
            ReplaceBuffer(ref _voxelStaticDirtyQueue, (ulong)(staticCap * sizeof(uint)));
            ReplaceBuffer(ref _voxelStaticActiveDirtyList, (ulong)(staticCap * 2 * sizeof(uint)));
            ReplaceBuffer(ref _voxelStaticBrickPool, (ulong)staticCap * 512UL * 16UL);
            ReplaceBuffer(ref _voxelStaticPropBuffer, (ulong)staticCap * 512UL * 4UL);

            uint mips = 1;
            int dim = res;
            while (dim >= 8)
            {
                dim >>= 1;
                ++mips;
            }
            _voxelVolumeMipCount = mips;
            for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
            {
                _voxelVolumes[c]?.Dispose();
                _voxelVolumes[c] = _system.CreateTexture(new TextureDesc
                {
                    Type = TextureType.ThreeD,
                    Extent = new Extent3D((uint)res, (uint)res, (uint)res),
                    MipLevels = mips,
                    LayerCount = 1,
                    Format = Format.Rgba16Float,
                    Usage = TextureUsage.Sampled | TextureUsage.Storage,
                });
            }

            _voxelNeedsInit = true;
            _voxelGiForceReset = true;
            _giHistoryValid = false;
            _voxelClipmap.Invalidate();
        }

        bool dynChanged = _voxelAllocResolution != res
            || _voxelAllocDynamicCapacity != dynCap
            || _voxelAllocDynamicLevels != dynLevels;
        if (dynChanged)
        {
            if (dynCap > 0 && dynLevels > 0)
            {
                int bpa = res / World3DVoxelClipmap.BrickSize;
                int dynSlots = dynLevels * bpa * bpa * bpa;
                int dynWords = (dynSlots + 31) / 32;
                ReplaceBuffer(ref _voxelDynamicDesiredMask, (ulong)(dynWords * sizeof(uint)));
                ReplaceBuffer(ref _voxelDynamicPageTable, (ulong)(dynSlots * sizeof(uint)));
                ReplaceBuffer(ref _voxelDynamicReverseTable, (ulong)(dynCap * sizeof(uint)));
                ReplaceBuffer(ref _voxelDynamicActiveList, (ulong)(dynCap * 2 * sizeof(uint)));
                ReplaceBuffer(ref _voxelDynamicBrickPool, (ulong)dynCap * 512UL * 16UL);
                ReplaceBuffer(ref _voxelDynamicPropBuffer, (ulong)dynCap * 512UL * 4UL);
            }
            else
            {
                DisposeDynamicBuffers();
            }
        }

        _voxelAllocResolution = res;
        _voxelAllocStaticCapacity = staticCap;
        _voxelAllocDynamicCapacity = dynCap;
        _voxelAllocDynamicLevels = dynLevels;
    }

    private void EnsureVoxelGiScreenTargets(Extent2D viewport, bool halfRes)
    {
        Extent2D traceExt = halfRes
            ? new Extent2D(Math.Max(1u, (viewport.X + 1) / 2), Math.Max(1u, (viewport.Y + 1) / 2))
            : viewport;

        bool traceChanged = _giRawDiffuse == null || !_giRawDiffuse.Extent.Equals(new Extent3D(traceExt.X, traceExt.Y, 1));
        if (traceChanged)
        {
            DisposeGiScreenTextures();
            TextureDesc traceDesc = new()
            {
                Type = TextureType.TwoD,
                Extent = new Extent3D(traceExt.X, traceExt.Y, 1),
                Format = Format.Rgba16Float,
                Usage = TextureUsage.Sampled | TextureUsage.Storage,
            };
            _giRawDiffuse = _system.CreateTexture(traceDesc);
            _giRawSpecular = _system.CreateTexture(traceDesc);
            _giResolvedDiffuse = _system.CreateTexture(traceDesc);
            _giResolvedSpecular = _system.CreateTexture(traceDesc);
            _giHistoryDiffuse[0] = _system.CreateTexture(traceDesc);
            _giHistoryDiffuse[1] = _system.CreateTexture(traceDesc);
            _giHistorySpecular[0] = _system.CreateTexture(traceDesc);
            _giHistorySpecular[1] = _system.CreateTexture(traceDesc);
            _giHistoryValid = false;
            _giSubmittedHistoryIndex = -1;
        }

        if (halfRes)
        {
            if (_giFullDiffuse == null || !_giFullDiffuse.Extent.Equals(new Extent3D(viewport.X, viewport.Y, 1)))
            {
                _giFullDiffuse?.Dispose();
                _giFullSpecular?.Dispose();
                TextureDesc fullDesc = new()
                {
                    Type = TextureType.TwoD,
                    Extent = new Extent3D(viewport.X, viewport.Y, 1),
                    Format = Format.Rgba16Float,
                    Usage = TextureUsage.Sampled | TextureUsage.Storage,
                };
                _giFullDiffuse = _system.CreateTexture(fullDesc);
                _giFullSpecular = _system.CreateTexture(fullDesc);
            }
        }
        else
        {
            _giFullDiffuse?.Dispose();
            _giFullDiffuse = null;
            _giFullSpecular?.Dispose();
            _giFullSpecular = null;
        }

        _giScreenViewport = viewport;
        _giScreenHalfRes = halfRes;
    }

    private void EnsureExactBuffer(ref GpuBuffer? buffer, ulong bytes)
    {
        if (buffer != null && buffer.SizeBytes >= bytes)
            return;
        buffer?.Dispose();
        buffer = _system.CreateBuffer(bytes, MemoryType.GpuOnly);
    }

    private void ReplaceBuffer(ref GpuBuffer? buffer, ulong bytes)
    {
        buffer?.Dispose();
        buffer = _system.CreateBuffer(bytes, MemoryType.GpuOnly);
    }

    private void DisposeDynamicBuffers()
    {
        _voxelDynamicDesiredMask?.Dispose(); _voxelDynamicDesiredMask = null;
        _voxelDynamicPageTable?.Dispose(); _voxelDynamicPageTable = null;
        _voxelDynamicReverseTable?.Dispose(); _voxelDynamicReverseTable = null;
        _voxelDynamicActiveList?.Dispose(); _voxelDynamicActiveList = null;
        _voxelDynamicBrickPool?.Dispose(); _voxelDynamicBrickPool = null;
        _voxelDynamicPropBuffer?.Dispose(); _voxelDynamicPropBuffer = null;
    }

    private void DisposeGiScreenTextures()
    {
        _giRawDiffuse?.Dispose(); _giRawDiffuse = null;
        _giRawSpecular?.Dispose(); _giRawSpecular = null;
        _giResolvedDiffuse?.Dispose(); _giResolvedDiffuse = null;
        _giResolvedSpecular?.Dispose(); _giResolvedSpecular = null;
        _giHistoryDiffuse[0]?.Dispose(); _giHistoryDiffuse[0] = null;
        _giHistoryDiffuse[1]?.Dispose(); _giHistoryDiffuse[1] = null;
        _giHistorySpecular[0]?.Dispose(); _giHistorySpecular[0] = null;
        _giHistorySpecular[1]?.Dispose(); _giHistorySpecular[1] = null;
        _giFullDiffuse?.Dispose(); _giFullDiffuse = null;
        _giFullSpecular?.Dispose(); _giFullSpecular = null;
    }

    private void DisposeVoxelGi()
    {
        for (int i = 0; i < _voxelVolumes.Length; ++i)
        {
            _voxelVolumes[i]?.Dispose();
            _voxelVolumes[i] = null;
        }
        _voxelBlueNoise?.Dispose(); _voxelBlueNoise = null;
        DisposeGiScreenTextures();
        _voxelDummyBuffer?.Dispose(); _voxelDummyBuffer = null;
        _voxelPoolState?.Dispose(); _voxelPoolState = null;
        _voxelCounters?.Dispose(); _voxelCounters = null;
        _voxelStaticFreeList?.Dispose(); _voxelStaticFreeList = null;
        _voxelStaticPageTable?.Dispose(); _voxelStaticPageTable = null;
        _voxelStaticReverseTable?.Dispose(); _voxelStaticReverseTable = null;
        _voxelStaticDirtyMask?.Dispose(); _voxelStaticDirtyMask = null;
        _voxelStaticDirtyQueue?.Dispose(); _voxelStaticDirtyQueue = null;
        _voxelStaticActiveDirtyList?.Dispose(); _voxelStaticActiveDirtyList = null;
        _voxelStaticBrickPool?.Dispose(); _voxelStaticBrickPool = null;
        _voxelStaticPropBuffer?.Dispose(); _voxelStaticPropBuffer = null;
        DisposeDynamicBuffers();
        _voxelCandidateList?.Dispose(); _voxelCandidateList = null;
        _voxelChunkList?.Dispose(); _voxelChunkList = null;
        _voxelIndirectArgs?.Dispose(); _voxelIndirectArgs = null;
        _voxelSurfaceShaders.Clear();
    }

    private sealed class VoxelGiPasses(World3DScene scene) : IRenderGraphNode
    {
        private const int PassInit = 1;
        private const int PassBlueNoise = 2;
        private const int PassResetAndEvict = 3;
        private const int PassSelectDirty = 4;
        private const int PassPopAndBind = 5;
        private const int PassCullInstances = 6;
        private const int PassAllocateDynamic = 7;
        private const int PassPreparePreExpand = 8;
        private const int PassClearStatic = 9;
        private const int PassClearDynamic = 10;
        private const int PassExpandChunks = 11;
        private const int PassPreparePostExpand = 12;
        private const int PassReclaimEmpty = 13;
        private const int PassPrepareFinalize = 14;
        private const int PassInjectStatic = 15;
        private const int PassInjectDynamic = 16;
        private const int PassPropGatherStatic = 17;
        private const int PassPropApplyStatic = 18;
        private const int PassPropGatherDynamic = 19;
        private const int PassPropApplyDynamic = 20;
        private const int PassTrace = 21;
        private const int PassDemosaic = 22;
        private const int PassUpsample = 23;
        private const int PassVoxelizeBase = 1000;
        private const int PassClearVolBase = 2000;
        private const int PassMip0Base = 3000;
        private const int PassMip1Base = 4000;

        private readonly World3DScene _scene = scene;
        private readonly RGTexture[] _volRG = new RGTexture[World3DVoxelClipmap.CascadeCount];
        private RGTexture _blueNoiseRG;
        private RGTexture _rawDiffRG;
        private RGTexture _rawSpecRG;
        private RGTexture _resolvedDiffRG;
        private RGTexture _resolvedSpecRG;
        private RGTexture _prevHistDiffRG;
        private RGTexture _prevHistSpecRG;
        private RGTexture _currHistDiffRG;
        private RGTexture _currHistSpecRG;
        private RGTexture _fullDiffRG;
        private RGTexture _fullSpecRG;
        private RGBuffer _poolStateRG;
        private RGBuffer _countersRG;
        private RGBuffer _staticFreeListRG;
        private RGBuffer _staticPageTableRG;
        private RGBuffer _staticReverseTableRG;
        private RGBuffer _staticDirtyMaskRG;
        private RGBuffer _staticDirtyQueueRG;
        private RGBuffer _staticActiveDirtyRG;
        private RGBuffer _staticBrickPoolRG;
        private RGBuffer _staticPropRG;
        private RGBuffer _dynDesiredMaskRG;
        private RGBuffer _dynPageTableRG;
        private RGBuffer _dynReverseTableRG;
        private RGBuffer _dynActiveListRG;
        private RGBuffer _dynBrickPoolRG;
        private RGBuffer _dynPropRG;
        private RGBuffer _candidatesRG;
        private RGBuffer _chunksRG;
        private RGBuffer _indirectArgsRG;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            if (!_scene._recordedVoxelGiEnabled)
                return;

            GBufferTargets gbuffer = _scene._gbufferTargets;
            if (!builder.HasOutput || !gbuffer.Depth.IsValid)
                return;

            _poolStateRG = builder.ImportBuffer(_scene._voxelPoolState!, "World3D.Voxel.PoolState");
            _countersRG = builder.ImportBuffer(_scene._voxelCounters!, "World3D.Voxel.Counters");
            _staticFreeListRG = builder.ImportBuffer(_scene._voxelStaticFreeList!, "World3D.Voxel.StaticFreeList");
            _staticPageTableRG = builder.ImportBuffer(_scene._voxelStaticPageTable!, "World3D.Voxel.StaticPageTable");
            _staticReverseTableRG = builder.ImportBuffer(_scene._voxelStaticReverseTable!, "World3D.Voxel.StaticReverseTable");
            _staticDirtyMaskRG = builder.ImportBuffer(_scene._voxelStaticDirtyMask!, "World3D.Voxel.StaticDirtyMask");
            _staticDirtyQueueRG = builder.ImportBuffer(_scene._voxelStaticDirtyQueue!, "World3D.Voxel.StaticDirtyQueue");
            _staticActiveDirtyRG = builder.ImportBuffer(_scene._voxelStaticActiveDirtyList!, "World3D.Voxel.StaticActiveDirty");
            _staticBrickPoolRG = builder.ImportBuffer(_scene._voxelStaticBrickPool!, "World3D.Voxel.StaticBrickPool");
            _staticPropRG = builder.ImportBuffer(_scene._voxelStaticPropBuffer!, "World3D.Voxel.StaticProp");
            _candidatesRG = builder.ImportBuffer(_scene._voxelCandidateList!, "World3D.Voxel.Candidates");
            _chunksRG = builder.ImportBuffer(_scene._voxelChunkList!, "World3D.Voxel.Chunks");
            _indirectArgsRG = builder.ImportBuffer(_scene._voxelIndirectArgs!, "World3D.Voxel.IndirectArgs");

            bool dynActive = _scene._recordedVoxelDynamicActive && _scene._voxelDynamicBrickPool != null;
            if (dynActive)
            {
                _dynDesiredMaskRG = builder.ImportBuffer(_scene._voxelDynamicDesiredMask!, "World3D.Voxel.DynDesiredMask");
                _dynPageTableRG = builder.ImportBuffer(_scene._voxelDynamicPageTable!, "World3D.Voxel.DynPageTable");
                _dynReverseTableRG = builder.ImportBuffer(_scene._voxelDynamicReverseTable!, "World3D.Voxel.DynReverseTable");
                _dynActiveListRG = builder.ImportBuffer(_scene._voxelDynamicActiveList!, "World3D.Voxel.DynActiveList");
                _dynBrickPoolRG = builder.ImportBuffer(_scene._voxelDynamicBrickPool!, "World3D.Voxel.DynBrickPool");
                _dynPropRG = builder.ImportBuffer(_scene._voxelDynamicPropBuffer!, "World3D.Voxel.DynProp");
            }

            for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                _volRG[c] = builder.ImportTexture(_scene._voxelVolumes[c]!, $"World3D.Voxel.Vol{c}");

            _blueNoiseRG = builder.ImportTexture(_scene._voxelBlueNoise!, "World3D.Voxel.BlueNoise");
            _rawDiffRG = builder.ImportTexture(_scene._giRawDiffuse!, "World3D.Voxel.RawDiffuse");
            _rawSpecRG = builder.ImportTexture(_scene._giRawSpecular!, "World3D.Voxel.RawSpecular");
            _resolvedDiffRG = builder.ImportTexture(_scene._giResolvedDiffuse!, "World3D.Voxel.ResolvedDiffuse");
            _resolvedSpecRG = builder.ImportTexture(_scene._giResolvedSpecular!, "World3D.Voxel.ResolvedSpecular");
            int currH = _scene._giHistoryWriteIndex;
            int prevH = 1 - currH;
            _currHistDiffRG = builder.ImportTexture(_scene._giHistoryDiffuse[currH]!, "World3D.Voxel.CurrHistDiff");
            _currHistSpecRG = builder.ImportTexture(_scene._giHistorySpecular[currH]!, "World3D.Voxel.CurrHistSpec");
            _prevHistDiffRG = builder.ImportTexture(_scene._giHistoryDiffuse[prevH]!, "World3D.Voxel.PrevHistDiff");
            _prevHistSpecRG = builder.ImportTexture(_scene._giHistorySpecular[prevH]!, "World3D.Voxel.PrevHistSpec");
            if (_scene._recordedVoxelHalfRes)
            {
                _fullDiffRG = builder.ImportTexture(_scene._giFullDiffuse!, "World3D.Voxel.FullDiffuse");
                _fullSpecRG = builder.ImportTexture(_scene._giFullSpecular!, "World3D.Voxel.FullSpecular");
            }

            if (_scene._voxelNeedsInit)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.Init", PassInit);
                p.Write(_poolStateRG, RGUsage.ComputeWrite);
                p.Write(_staticFreeListRG, RGUsage.ComputeWrite);
                p.Write(_staticPageTableRG, RGUsage.ComputeWrite);
                p.Write(_staticReverseTableRG, RGUsage.ComputeWrite);
                p.Write(_countersRG, RGUsage.ComputeWrite);
                p.ProducesOutput();
            }

            if (_scene._voxelBlueNoiseDirty)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.BlueNoise", PassBlueNoise);
                p.Write(_blueNoiseRG, RGUsage.ComputeWrite);
                p.ProducesOutput();
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.ResetEvict", PassResetAndEvict);
                p.ReadWrite(_poolStateRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticFreeListRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticPageTableRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticReverseTableRG, RGUsage.ComputeReadWrite);
                p.Write(_staticDirtyMaskRG, RGUsage.ComputeWrite);
                p.Write(_countersRG, RGUsage.ComputeWrite);
                if (dynActive)
                {
                    p.Write(_dynDesiredMaskRG, RGUsage.ComputeWrite);
                    p.Write(_dynPageTableRG, RGUsage.ComputeWrite);
                    p.Write(_dynReverseTableRG, RGUsage.ComputeWrite);
                }
                p.ProducesOutput();
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.SelectDirty", PassSelectDirty);
                p.Read(_staticPageTableRG, RGUsage.ComputeRead);
                p.ReadWrite(_poolStateRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                p.Write(_staticDirtyQueueRG, RGUsage.ComputeWrite);
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PopBind", PassPopAndBind);
                p.Read(_staticDirtyQueueRG, RGUsage.ComputeRead);
                p.Read(_staticFreeListRG, RGUsage.ComputeRead);
                p.ReadWrite(_poolStateRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticPageTableRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticReverseTableRG, RGUsage.ComputeReadWrite);
                p.Write(_staticDirtyMaskRG, RGUsage.ComputeWrite);
                p.Write(_staticActiveDirtyRG, RGUsage.ComputeWrite);
                p.ProducesOutput();
            }

            bool hasInstances = _scene._instances.End > 0 && _scene._gpu.Directory != null;
            if (hasInstances)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.Cull", PassCullInstances);
                _scene._gpu.ReadTables(builder, p, RGUsage.ComputeRead, materials: true);
                p.Read(_staticDirtyMaskRG, RGUsage.ComputeRead);
                if (dynActive)
                    p.ReadWrite(_dynDesiredMaskRG, RGUsage.ComputeReadWrite);
                p.Write(_candidatesRG, RGUsage.ComputeWrite);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
            }

            if (dynActive)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.AllocDyn", PassAllocateDynamic);
                p.Read(_dynDesiredMaskRG, RGUsage.ComputeRead);
                p.ReadWrite(_poolStateRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                p.Write(_dynPageTableRG, RGUsage.ComputeWrite);
                p.Write(_dynReverseTableRG, RGUsage.ComputeWrite);
                p.Write(_dynActiveListRG, RGUsage.ComputeWrite);
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PrepPre", PassPreparePreExpand);
                p.Read(_poolStateRG, RGUsage.ComputeRead);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                p.Write(_indirectArgsRG, RGUsage.ComputeWrite);
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.ClearStatic", PassClearStatic);
                p.Read(_indirectArgsRG, RGUsage.IndirectRead);
                p.Read(_staticActiveDirtyRG, RGUsage.ComputeRead);
                p.Read(_countersRG, RGUsage.ComputeRead);
                p.ReadWrite(_staticBrickPoolRG, RGUsage.ComputeReadWrite);
                p.ProducesOutput();
            }

            if (dynActive)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.ClearDyn", PassClearDynamic);
                p.Read(_indirectArgsRG, RGUsage.IndirectRead);
                p.Read(_dynActiveListRG, RGUsage.ComputeRead);
                p.Read(_countersRG, RGUsage.ComputeRead);
                p.ReadWrite(_dynBrickPoolRG, RGUsage.ComputeReadWrite);
            }

            if (hasInstances)
            {
                {
                    RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.ExpandChunks", PassExpandChunks);
                    p.Read(_indirectArgsRG, RGUsage.IndirectRead);
                    p.Read(_candidatesRG, RGUsage.ComputeRead);
                    p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                    p.Write(_chunksRG, RGUsage.ComputeWrite);
                }

                {
                    RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PrepPost", PassPreparePostExpand);
                    p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                    p.ReadWrite(_indirectArgsRG, RGUsage.ComputeReadWrite);
                }

                for (int bi = 0; bi < _scene._activeSurfaceBuckets.Count; ++bi)
                {
                    uint bucket = _scene._activeSurfaceBuckets[bi];
                    RenderGraphPassBuilder p = builder.AddPass($"World3D.Voxel.Voxelize{bucket}", PassVoxelizeBase + bi);
                    p.Read(_indirectArgsRG, RGUsage.IndirectRead);
                    _scene._gpu.ReadTables(builder, p, RGUsage.ComputeRead, materials: true);
                    foreach (DrawBucket db in _scene._drawLayout.Buckets)
                        p.Read(builder.ImportBuffer(db.Key.Page, "World3D.GeometryPayload"), RGUsage.ComputeRead);
                    p.Read(_staticDirtyMaskRG, RGUsage.ComputeRead);
                    p.Read(_staticPageTableRG, RGUsage.ComputeRead);
                    p.Read(_candidatesRG, RGUsage.ComputeRead);
                    p.Read(_chunksRG, RGUsage.ComputeRead);
                    p.Read(_countersRG, RGUsage.ComputeRead);
                    p.ReadWrite(_staticBrickPoolRG, RGUsage.ComputeReadWrite);
                    if (dynActive)
                    {
                        p.Read(_dynDesiredMaskRG, RGUsage.ComputeRead);
                        p.Read(_dynPageTableRG, RGUsage.ComputeRead);
                        p.ReadWrite(_dynBrickPoolRG, RGUsage.ComputeReadWrite);
                    }
                    p.ProducesOutput();
                }
            }
            else
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PrepPostEmpty", PassPreparePostExpand);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_indirectArgsRG, RGUsage.ComputeReadWrite);
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.ReclaimEmpty", PassReclaimEmpty);
                p.Read(_indirectArgsRG, RGUsage.IndirectRead);
                p.Read(_staticActiveDirtyRG, RGUsage.ComputeRead);
                p.Read(_staticBrickPoolRG, RGUsage.ComputeRead);
                p.ReadWrite(_poolStateRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticFreeListRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticReverseTableRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_staticPageTableRG, RGUsage.ComputeReadWrite);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                p.ProducesOutput();
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.FinalizeCounters", PassPrepareFinalize);
                p.Read(_poolStateRG, RGUsage.ComputeRead);
                p.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);
                p.ProducesOutput();
            }

            for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
            {
                RenderGraphPassBuilder p = builder.AddPass($"World3D.Voxel.ClearVol{c}", PassClearVolBase + c);
                p.Write(_volRG[c], RGUsage.ComputeWrite);
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.InjectStatic", PassInjectStatic);
                DeclareCommonInjectReads(builder, p);
                p.Read(_staticReverseTableRG, RGUsage.ComputeRead);
                p.Read(_staticBrickPoolRG, RGUsage.ComputeRead);
                for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                    p.ReadWrite(_volRG[c], RGUsage.ComputeReadWrite);
            }

            if (dynActive)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.InjectDyn", PassInjectDynamic);
                DeclareCommonInjectReads(builder, p);
                p.Read(_indirectArgsRG, RGUsage.IndirectRead);
                p.Read(_countersRG, RGUsage.ComputeRead);
                p.Read(_dynReverseTableRG, RGUsage.ComputeRead);
                p.Read(_dynBrickPoolRG, RGUsage.ComputeRead);
                for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                    p.ReadWrite(_volRG[c], RGUsage.ComputeReadWrite);
            }

            SetupMipPasses(builder, PassMip0Base, "Mip0");

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PropGatherStatic", PassPropGatherStatic);
                for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                    p.Read(_volRG[c], RGUsage.ComputeRead);
                p.Read(_staticReverseTableRG, RGUsage.ComputeRead);
                p.Read(_staticBrickPoolRG, RGUsage.ComputeRead);
                p.Write(_staticPropRG, RGUsage.ComputeWrite);
            }

            if (dynActive)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PropGatherDyn", PassPropGatherDynamic);
                for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                    p.Read(_volRG[c], RGUsage.ComputeRead);
                p.Read(_countersRG, RGUsage.ComputeRead);
                p.Read(_dynReverseTableRG, RGUsage.ComputeRead);
                p.Read(_dynBrickPoolRG, RGUsage.ComputeRead);
                p.Write(_dynPropRG, RGUsage.ComputeWrite);
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PropApplyStatic", PassPropApplyStatic);
                p.Read(_staticReverseTableRG, RGUsage.ComputeRead);
                p.Read(_staticPropRG, RGUsage.ComputeRead);
                for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                    p.ReadWrite(_volRG[c], RGUsage.ComputeReadWrite);
            }

            if (dynActive)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.PropApplyDyn", PassPropApplyDynamic);
                p.Read(_countersRG, RGUsage.ComputeRead);
                p.Read(_dynReverseTableRG, RGUsage.ComputeRead);
                p.Read(_dynPropRG, RGUsage.ComputeRead);
                for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                    p.ReadWrite(_volRG[c], RGUsage.ComputeReadWrite);
            }

            SetupMipPasses(builder, PassMip1Base, "Mip1");

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.Trace", PassTrace);
                p.Read(builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views"), RGUsage.ComputeRead);
                if (!_scene._frameShadowData.IsEmpty)
                    p.Read(builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame"), RGUsage.ComputeRead);
                p.Read(gbuffer.Depth, RGUsage.ComputeRead);
                p.Read(gbuffer.Normal, RGUsage.ComputeRead);
                p.Read(gbuffer.Emissive, RGUsage.ComputeRead);
                p.Read(gbuffer.Material, RGUsage.ComputeRead);
                p.Read(_blueNoiseRG, RGUsage.ComputeRead);
                for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
                    p.Read(_volRG[c], RGUsage.ComputeRead);
                if (_scene._recordedVoxelHasRsm)
                {
                    if (_scene._frameRsmFluxRG.IsValid) p.Read(_scene._frameRsmFluxRG, RGUsage.ComputeRead);
                    if (_scene._frameRsmNormalRG.IsValid) p.Read(_scene._frameRsmNormalRG, RGUsage.ComputeRead);
                    if (_scene._frameRsmDepthRG.IsValid) p.Read(_scene._frameRsmDepthRG, RGUsage.ComputeRead);
                }
                p.Write(_rawDiffRG, RGUsage.ComputeWrite);
                p.Write(_rawSpecRG, RGUsage.ComputeWrite);
            }

            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.Demosaic", PassDemosaic);
                p.Read(gbuffer.Depth, RGUsage.ComputeRead);
                p.Read(gbuffer.Normal, RGUsage.ComputeRead);
                p.Read(_rawDiffRG, RGUsage.ComputeRead);
                p.Read(_rawSpecRG, RGUsage.ComputeRead);
                p.Read(_prevHistDiffRG, RGUsage.ComputeRead);
                p.Read(_prevHistSpecRG, RGUsage.ComputeRead);
                p.Write(_resolvedDiffRG, RGUsage.ComputeWrite);
                p.Write(_resolvedSpecRG, RGUsage.ComputeWrite);
                p.Write(_currHistDiffRG, RGUsage.ComputeWrite);
                p.Write(_currHistSpecRG, RGUsage.ComputeWrite);
                p.ProducesOutput();
            }

            if (_scene._recordedVoxelHalfRes)
            {
                RenderGraphPassBuilder p = builder.AddPass("World3D.Voxel.Upsample", PassUpsample);
                p.Read(gbuffer.Depth, RGUsage.ComputeRead);
                p.Read(gbuffer.Normal, RGUsage.ComputeRead);
                p.Read(_resolvedDiffRG, RGUsage.ComputeRead);
                p.Read(_resolvedSpecRG, RGUsage.ComputeRead);
                p.Write(_fullDiffRG, RGUsage.ComputeWrite);
                p.Write(_fullSpecRG, RGUsage.ComputeWrite);
                _scene._frameGiOutputDiffuseRG = _fullDiffRG;
                _scene._frameGiOutputSpecularRG = _fullSpecRG;
            }
            else
            {
                _scene._frameGiOutputDiffuseRG = _resolvedDiffRG;
                _scene._frameGiOutputSpecularRG = _resolvedSpecRG;
            }
            _scene._frameGiRawDiffuseRG = _rawDiffRG;
        }

        private void DeclareCommonInjectReads(RenderGraphBuilder builder, RenderGraphPassBuilder p)
        {
            p.Read(builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views"), RGUsage.ComputeRead);
            if (!_scene._frameShadowData.IsEmpty)
                p.Read(builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame"), RGUsage.ComputeRead);
            if (_scene._frameShadowArrayRG.IsValid) p.Read(_scene._frameShadowArrayRG, RGUsage.ComputeRead);
            if (_scene._frameShadowMinMaxRG.IsValid) p.Read(_scene._frameShadowMinMaxRG, RGUsage.ComputeRead);
            if (_scene._frameFarHeightRG.IsValid) p.Read(_scene._frameFarHeightRG, RGUsage.ComputeRead);
            if (_scene._frameCloudShadowRG.IsValid) p.Read(_scene._frameCloudShadowRG, RGUsage.ComputeRead);
            if (_scene._recordedVoxelHasRsm)
            {
                if (_scene._frameRsmFluxRG.IsValid) p.Read(_scene._frameRsmFluxRG, RGUsage.ComputeRead);
                if (_scene._frameRsmDepthRG.IsValid) p.Read(_scene._frameRsmDepthRG, RGUsage.ComputeRead);
            }
        }

        private void SetupMipPasses(RenderGraphBuilder builder, int baseId, string prefix)
        {
            int count = _scene._voxelMipDispatches.Count;
            for (int c = 0; c < World3DVoxelClipmap.CascadeCount; ++c)
            {
                for (int s = 0; s < count; ++s)
                {
                    RenderGraphPassBuilder p = builder.AddPass($"World3D.Voxel.{prefix}.C{c}S{s}", baseId + c * 16 + s);
                    p.ReadWrite(_volRG[c], RGUsage.ComputeReadWrite);
                }
            }
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            if (passId >= PassMip1Base)
            {
                int offset = passId - PassMip1Base;
                ExecuteMipDispatch(in context, offset / 16, offset % 16);
                return;
            }
            if (passId >= PassMip0Base)
            {
                int offset = passId - PassMip0Base;
                ExecuteMipDispatch(in context, offset / 16, offset % 16);
                return;
            }
            if (passId >= PassClearVolBase)
            {
                ExecuteClearVolume(in context, passId - PassClearVolBase);
                return;
            }
            if (passId >= PassVoxelizeBase)
            {
                ExecuteVoxelize(in context, _scene._activeSurfaceBuckets[passId - PassVoxelizeBase]);
                return;
            }

            switch (passId)
            {
                case PassInit: ExecuteInit(in context); break;
                case PassBlueNoise: ExecuteBlueNoise(in context); break;
                case PassResetAndEvict: ExecutePageManage(in context, 0u); break;
                case PassSelectDirty: ExecutePageManage(in context, 1u); break;
                case PassPopAndBind: ExecutePageManage(in context, 2u); break;
                case PassCullInstances: ExecuteCullInstances(in context); break;
                case PassAllocateDynamic: ExecutePageManage(in context, 3u); break;
                case PassPreparePreExpand: ExecutePrepareArgs(in context, 0u); break;
                case PassClearStatic: ExecuteClearPool(in context, dynamicPool: false); break;
                case PassClearDynamic: ExecuteClearPool(in context, dynamicPool: true); break;
                case PassExpandChunks: ExecuteExpandChunks(in context); break;
                case PassPreparePostExpand: ExecutePrepareArgs(in context, 1u); break;
                case PassReclaimEmpty: ExecutePageManage(in context, 4u); break;
                case PassPrepareFinalize: ExecutePrepareArgs(in context, 2u); break;
                case PassInjectStatic: ExecuteInject(in context, dynamicPool: false); break;
                case PassInjectDynamic: ExecuteInject(in context, dynamicPool: true); break;
                case PassPropGatherStatic: ExecutePropagate(in context, mode: 0u, dynamicPool: false); break;
                case PassPropGatherDynamic: ExecutePropagate(in context, mode: 0u, dynamicPool: true); break;
                case PassPropApplyStatic: ExecutePropagate(in context, mode: 1u, dynamicPool: false); break;
                case PassPropApplyDynamic: ExecutePropagate(in context, mode: 1u, dynamicPool: true); break;
                case PassTrace: ExecuteTrace(in context); break;
                case PassDemosaic: ExecuteDemosaic(in context); break;
                case PassUpsample: ExecuteUpsample(in context); break;
            }
        }

        private void ExecuteInit(in RenderGraphContext context)
        {
            uint bpa = (uint)_scene._recordedVoxelPlan.BricksPerAxis;
            uint totalSlots = World3DVoxelClipmap.CascadeCount * bpa * bpa * bpa;
            uint staticCap = (uint)_scene._voxelAllocStaticCapacity;
            uint itemCount = Math.Max(staticCap, totalSlots);

            VoxelInitRoot root = new(
                _scene._voxelPoolState!.View,
                _scene._voxelStaticFreeList!.View,
                _scene._voxelStaticPageTable!.View,
                _scene._voxelStaticReverseTable!.View,
                _scene._voxelCounters!.View,
                _scene._voxelStaticPropBuffer!.View,
                staticCap,
                totalSlots,
                itemCount,
                _scene._recordedVoxelBudgets[0],
                _scene._recordedVoxelBudgets[1],
                _scene._recordedVoxelBudgets[2],
                _scene._recordedVoxelBudgets[3],
                0u);
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelInitShader!, in root, new Extent3D((itemCount + 63u) / 64u, 1u, 1u));
        }

        private void ExecuteBlueNoise(in RenderGraphContext context)
        {
            VoxelBlueNoiseRoot root = new(
                _scene._voxelBlueNoise!.GetStorageId(0),
                64u,
                1u,
                0u);
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelBlueNoiseShader!, in root, new Extent3D(8u, 8u, 1u));
        }

        private void ExecutePageManage(in RenderGraphContext context, uint mode)
        {
            uint bpa = (uint)_scene._recordedVoxelPlan.BricksPerAxis;
            uint perCascade = bpa * bpa * bpa;
            uint totalStaticSlots = World3DVoxelClipmap.CascadeCount * perCascade;
            uint totalDynSlots = (uint)_scene._recordedVoxelDynamicLevels * perCascade;
            uint staticMaskWords = (totalStaticSlots + 31u) / 32u;
            uint dynMaskWords = _scene._recordedVoxelDynamicActive ? (totalDynSlots + 31u) / 32u : 0u;
            uint staticCap = (uint)_scene._voxelAllocStaticCapacity;
            uint dynCap = _scene._recordedVoxelDynamicActive ? (uint)_scene._voxelAllocDynamicCapacity : 0u;
            bool hasDyn = _scene._recordedVoxelDynamicActive && _scene._voxelDynamicBrickPool != null;
            GPUBufferView dummy = _scene._voxelDummyBuffer!.View;

            uint itemCount = mode switch
            {
                0u => Math.Max(totalStaticSlots, Math.Max(staticMaskWords, Math.Max(totalDynSlots, dynCap))),
                1u => totalStaticSlots,
                2u => staticCap,
                3u => totalDynSlots,
                _ => 0u,
            };

            VoxelPageManageRoot root = new(
                _scene._frameVoxelFrame.View,
                mode,
                itemCount);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            if (mode == 4u)
            {
                compute.DispatchIndirect(_scene._voxelPageManageShader!, in root,
                    _scene._voxelIndirectArgs!.Slice(4 * VoxelIndirectStride, 12));
            }
            else if (itemCount > 0u)
            {
                compute.Dispatch(_scene._voxelPageManageShader!, in root, new Extent3D((itemCount + 63u) / 64u, 1u, 1u));
            }
        }

        private void ExecuteCullInstances(in RenderGraphContext context)
        {
            uint instCount = (uint)_scene._instances.End;
            if (instCount == 0u || _scene._gpu.Directory == null)
                return;

            bool hasDyn = _scene._recordedVoxelDynamicActive && _scene._voxelDynamicBrickPool != null;
            VoxelCullInstancesRoot root = new(
                _scene._recordedView.OriginAnchorCell,
                _scene._gpu.Directory.View,
                _scene._frameVoxelCascades.View,
                _scene._voxelStaticDirtyMask!.View,
                hasDyn ? _scene._voxelDynamicDesiredMask!.View : _scene._voxelDummyBuffer!.View,
                _scene._voxelCandidateList!.View,
                _scene._voxelCounters!.View,
                instCount,
                VoxelMaxCandidates,
                (uint)_scene._recordedVoxelPlan.BricksPerAxis,
                (uint)_scene._recordedVoxelDynamicLevels,
                1u,
                hasDyn ? 1u : 0u,
                0u,
                0u);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelCullInstancesShader!, in root, new Extent3D((instCount + 63u) / 64u, 1u, 1u));
        }

        private void ExecutePrepareArgs(in RenderGraphContext context, uint mode)
        {
            VoxelPrepareArgsRoot root = new(
                _scene._voxelPoolState!.View,
                _scene._voxelCounters!.View,
                _scene._voxelIndirectArgs!.Slice(0 * VoxelIndirectStride, 16).View,
                _scene._voxelIndirectArgs!.Slice(1 * VoxelIndirectStride, 16).View,
                _scene._voxelIndirectArgs!.Slice(2 * VoxelIndirectStride, 16).View,
                _scene._voxelIndirectArgs!.Slice(3 * VoxelIndirectStride, 16).View,
                _scene._voxelIndirectArgs!.Slice(4 * VoxelIndirectStride, 16).View,
                _scene._voxelIndirectArgs!.Slice(5 * VoxelIndirectStride, 16).View,
                mode,
                (uint)_scene._voxelAllocStaticCapacity,
                _scene._recordedVoxelDynamicActive ? (uint)_scene._voxelAllocDynamicCapacity : 0u,
                VoxelMaxCandidates,
                VoxelMaxChunks,
                0u,
                0u,
                0u);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelPrepareArgsShader!, in root, new Extent3D(1u, 1u, 1u));
        }

        private void ExecuteClearPool(in RenderGraphContext context, bool dynamicPool)
        {
            uint capacity = dynamicPool ? (uint)_scene._voxelAllocDynamicCapacity : (uint)_scene._voxelAllocStaticCapacity;
            VoxelClearRoot root = new(
                dynamicPool ? _scene._voxelDynamicActiveList!.View : _scene._voxelStaticActiveDirtyList!.View,
                dynamicPool ? _scene._voxelDynamicBrickPool!.View : _scene._voxelStaticBrickPool!.View,
                _scene._voxelCounters!.View,
                GPUTextureId.Invalid,
                0u,
                dynamicPool ? 6u : 5u,
                capacity,
                0u);

            int slot = dynamicPool ? 1 : 0;
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.DispatchIndirect(_scene._voxelClearShader!, in root, _scene._voxelIndirectArgs!.Slice((ulong)slot * VoxelIndirectStride, 12));
        }

        private void ExecuteClearVolume(in RenderGraphContext context, int cascade)
        {
            uint res = (uint)_scene._recordedVoxelPlan.Resolution;
            VoxelClearRoot root = new(
                _scene._voxelDummyBuffer!.View,
                _scene._voxelDummyBuffer!.View,
                _scene._voxelCounters!.View,
                _scene._voxelVolumes[cascade]!.GetStorageId(0),
                1u,
                0u,
                0u,
                res);

            uint g = (res + 3u) / 4u;
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelClearShader!, in root, new Extent3D(g, g, g));
        }

        private void ExecuteExpandChunks(in RenderGraphContext context)
        {
            VoxelExpandChunksRoot root = new(
                _scene._voxelCandidateList!.View,
                _scene._voxelChunkList!.View,
                _scene._voxelCounters!.View,
                VoxelMaxCandidates,
                VoxelMaxChunks);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.DispatchIndirect(_scene._voxelExpandChunksShader!, in root, _scene._voxelIndirectArgs!.Slice(2 * VoxelIndirectStride, 12));
        }

        private void ExecuteVoxelize(in RenderGraphContext context, uint surfaceBucket)
        {
            string module = _scene._surfaceBucketModules[(int)surfaceBucket];
            Shader shader = _scene.GetVoxelSurfaceShader(module);
            bool hasDyn = _scene._recordedVoxelDynamicActive && _scene._voxelDynamicBrickPool != null;
            GPUBufferView dummy = _scene._voxelDummyBuffer!.View;

            VoxelVoxelizeRoot root = new(
                _scene._recordedView.OriginAnchorCell,
                _scene._gpu.Directory!.View,
                _scene._frameVoxelCascades.View,
                _scene._voxelStaticDirtyMask!.View,
                _scene._voxelStaticPageTable!.View,
                _scene._voxelStaticBrickPool!.View,
                hasDyn ? _scene._voxelDynamicDesiredMask!.View : dummy,
                hasDyn ? _scene._voxelDynamicPageTable!.View : dummy,
                hasDyn ? _scene._voxelDynamicBrickPool!.View : dummy,
                _scene._voxelCandidateList!.View,
                _scene._voxelChunkList!.View,
                _scene._voxelCounters!.View,
                (uint)_scene._recordedVoxelPlan.BricksPerAxis,
                (uint)_scene._voxelAllocStaticCapacity,
                hasDyn ? (uint)_scene._voxelAllocDynamicCapacity : 0u,
                VoxelMaxChunks,
                surfaceBucket,
                hasDyn ? 1u : 0u,
                0u,
                0u);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.DispatchIndirect(shader, in root, _scene._voxelIndirectArgs!.Slice(3 * VoxelIndirectStride, 12));
        }

        private void ExecuteInject(in RenderGraphContext context, bool dynamicPool)
        {
            uint capacity = dynamicPool ? (uint)_scene._voxelAllocDynamicCapacity : (uint)_scene._voxelAllocStaticCapacity;
            VoxelInjectRoot root = new(
                _scene._frameVoxelFrame.View,
                _scene._frameViews.View,
                _scene._frameShadowData.View,
                _scene._voxelVolumes[0]!.GetStorageId(0),
                _scene._voxelVolumes[1]!.GetStorageId(0),
                _scene._voxelVolumes[2]!.GetStorageId(0),
                _scene._voxelVolumes[3]!.GetStorageId(0),
                dynamicPool ? 1u : 0u,
                0u);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            if (dynamicPool)
                compute.DispatchIndirect(_scene._voxelInjectShader!, in root, _scene._voxelIndirectArgs!.Slice(5 * VoxelIndirectStride, 12));
            else
                compute.Dispatch(_scene._voxelInjectShader!, in root, new Extent3D(capacity, 1u, 1u));
        }

        private void ExecuteMipDispatch(in RenderGraphContext context, int cascade, int stepIdx)
        {
            MipDispatchDesc desc = _scene._voxelMipDispatches[stepIdx];
            Texture vol = _scene._voxelVolumes[cascade]!;
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            if (desc.IsChain)
            {
                VoxelMipChainRoot root = new(
                    GPUTextureId.FromSampled(vol),
                    vol.GetStorageId(desc.SrcMip + 1),
                    vol.GetStorageId(desc.SrcMip + 2),
                    vol.GetStorageId(desc.SrcMip + 3),
                    desc.SrcMip,
                    desc.Size1,
                    desc.Size2,
                    desc.Size3);
                uint g = (desc.Size1 + 3u) / 4u;
                compute.Dispatch(_scene._voxelMipChainShader!, in root, new Extent3D(g, g, g));
            }
            else
            {
                VoxelMipRoot root = new(
                    GPUTextureId.FromSampled(vol),
                    vol.GetStorageId(desc.SrcMip + 1),
                    desc.SrcMip,
                    desc.Size1,
                    0u,
                    0u);
                uint g = (desc.Size1 + 3u) / 4u;
                compute.Dispatch(_scene._voxelMipShader!, in root, new Extent3D(g, g, g));
            }
        }

        private void ExecutePropagate(in RenderGraphContext context, uint mode, bool dynamicPool)
        {
            uint capacity = dynamicPool ? (uint)_scene._voxelAllocDynamicCapacity : (uint)_scene._voxelAllocStaticCapacity;
            VoxelPropagateRoot root = new(
                _scene._frameVoxelCascades.View,
                dynamicPool ? _scene._voxelDynamicReverseTable!.View : _scene._voxelStaticReverseTable!.View,
                dynamicPool ? _scene._voxelDynamicBrickPool!.View : _scene._voxelStaticBrickPool!.View,
                dynamicPool ? _scene._voxelDynamicPropBuffer!.View : _scene._voxelStaticPropBuffer!.View,
                _scene._voxelCounters!.View,
                mode == 0u ? GPUTextureId.FromSampled(_scene._voxelVolumes[0]!) : _scene._voxelVolumes[0]!.GetStorageId(0),
                mode == 0u ? GPUTextureId.FromSampled(_scene._voxelVolumes[1]!) : _scene._voxelVolumes[1]!.GetStorageId(0),
                mode == 0u ? GPUTextureId.FromSampled(_scene._voxelVolumes[2]!) : _scene._voxelVolumes[2]!.GetStorageId(0),
                mode == 0u ? GPUTextureId.FromSampled(_scene._voxelVolumes[3]!) : _scene._voxelVolumes[3]!.GetStorageId(0),
                Math.Clamp(_scene.VoxelGi.PropagationDecay, 0f, 1.5f),
                mode,
                dynamicPool ? 1u : 0u,
                (uint)_scene._recordedVoxelPlan.BricksPerAxis,
                capacity,
                0u,
                0u,
                0u);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            if (dynamicPool)
                compute.DispatchIndirect(_scene._voxelPropagateShader!, in root, _scene._voxelIndirectArgs!.Slice(5 * VoxelIndirectStride, 12));
            else
                compute.Dispatch(_scene._voxelPropagateShader!, in root, new Extent3D(capacity, 1u, 1u));
        }

        private void ExecuteTrace(in RenderGraphContext context)
        {
            GBufferTargets gbuffer = _scene._gbufferTargets;
            Extent3D traceExt = _scene._giRawDiffuse!.Extent;

            VoxelTraceRoot root = new(
                _scene._frameVoxelFrame.View,
                _scene._frameViews.View,
                _scene._frameShadowData.View,
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Depth)),
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Normal)),
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Emissive)),
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Material)),
                GPUTextureId.FromSampled(_scene._voxelVolumes[0]!),
                GPUTextureId.FromSampled(_scene._voxelVolumes[1]!),
                GPUTextureId.FromSampled(_scene._voxelVolumes[2]!),
                GPUTextureId.FromSampled(_scene._voxelVolumes[3]!),
                _scene._giRawDiffuse!.GetStorageId(0),
                _scene._giRawSpecular!.GetStorageId(0));

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelTraceShader!, in root, new Extent3D((traceExt.X + 7u) / 8u, (traceExt.Y + 7u) / 8u, 1u));
        }

        private void ExecuteDemosaic(in RenderGraphContext context)
        {
            GBufferTargets gbuffer = _scene._gbufferTargets;
            Extent3D traceExt = _scene._giResolvedDiffuse!.Extent;
            Extent2D fullExt = _scene._giScreenViewport;
            int currH = _scene._giHistoryWriteIndex;
            int prevH = 1 - currH;
            bool hasHistory = _scene._recordedVoxelTemporalAlpha < 0.999f;

            VoxelDemosaicRoot root = new(
                _scene._frameVoxelGiViews.View,
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Depth)),
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Normal)),
                GPUTextureId.FromSampled(_scene._giRawDiffuse!),
                GPUTextureId.FromSampled(_scene._giRawSpecular!),
                GPUTextureId.FromSampled(_scene._giHistoryDiffuse[prevH]!),
                GPUTextureId.FromSampled(_scene._giHistorySpecular[prevH]!),
                _scene._giResolvedDiffuse!.GetStorageId(0),
                _scene._giResolvedSpecular!.GetStorageId(0),
                _scene._giHistoryDiffuse[currH]!.GetStorageId(0),
                _scene._giHistorySpecular[currH]!.GetStorageId(0),
                traceExt.X,
                traceExt.Y,
                fullExt.X,
                fullExt.Y,
                _scene._recordedVoxelHalfRes ? 1u : 0u,
                (uint)(_scene._voxelFrameCounter & 1023u),
                hasHistory ? 1u : 0u,
                _scene._recordedVoxelTemporalAlpha);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelDemosaicShader!, in root, new Extent3D((traceExt.X + 7u) / 8u, (traceExt.Y + 7u) / 8u, 1u));
        }

        private void ExecuteUpsample(in RenderGraphContext context)
        {
            GBufferTargets gbuffer = _scene._gbufferTargets;
            Extent2D fullExt = _scene._giScreenViewport;
            Extent3D halfExt = _scene._giResolvedDiffuse!.Extent;

            VoxelUpsampleRoot root = new(
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Depth)),
                GPUTextureId.FromSampled(context.GetTexture(gbuffer.Normal)),
                GPUTextureId.FromSampled(_scene._giResolvedDiffuse!),
                GPUTextureId.FromSampled(_scene._giResolvedSpecular!),
                _scene._giFullDiffuse!.GetStorageId(0),
                _scene._giFullSpecular!.GetStorageId(0),
                fullExt.X,
                fullExt.Y,
                halfExt.X,
                halfExt.Y,
                (uint)(_scene._voxelFrameCounter & 1023u),
                0u);

            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
            compute.Dispatch(_scene._voxelUpsampleShader!, in root, new Extent3D((fullExt.X + 7u) / 8u, (fullExt.Y + 7u) / 8u, 1u));
        }
    }

    private readonly record struct StaticDirtyBounds(World3DPosition MinWorld, World3DPosition MaxWorld);
    private readonly record struct MipDispatchDesc(bool IsChain, uint SrcMip, uint Size1, uint Size2, uint Size3);

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly record struct VoxelCandidateGpu(uint InstanceSlot, uint CascadeIndex, uint Lod, uint TriangleCount);

    [StructLayout(LayoutKind.Sequential, Size = 8)]
    private readonly record struct VoxelChunkGpu(uint CandidateIndex, uint StartTriangle);

    [StructLayout(LayoutKind.Sequential, Size = 80)]
    private readonly record struct VoxelInitRoot(
        GPUBufferView PoolState, GPUBufferView StaticFreeList, GPUBufferView StaticPageTable,
        GPUBufferView StaticReverseTable, GPUBufferView StaticCounters, GPUBufferView StaticPropBuffer,
        uint StaticCapacity, uint TotalStaticSlots, uint ItemCount,
        uint Budget0, uint Budget1, uint Budget2, uint Budget3, uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly record struct VoxelBlueNoiseRoot(GPUTextureId OutputLut, uint Size, uint Seed, uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly record struct VoxelPageManageRoot(GPUBufferView Frame, uint Mode, uint ItemCount);

    [StructLayout(LayoutKind.Explicit, Size = 96)]
    private readonly record struct VoxelCullInstancesRoot(
        [field: FieldOffset(0)] World3DCell OriginAnchorCell,
        [field: FieldOffset(16)] GPUBufferView Directory,
        [field: FieldOffset(24)] GPUBufferView Cascades,
        [field: FieldOffset(32)] GPUBufferView StaticDirtyMask,
        [field: FieldOffset(40)] GPUBufferView DynamicDesiredMask,
        [field: FieldOffset(48)] GPUBufferView CandidateList,
        [field: FieldOffset(56)] GPUBufferView Counters,
        [field: FieldOffset(64)] uint InstanceCount,
        [field: FieldOffset(68)] uint MaxCandidates,
        [field: FieldOffset(72)] uint BricksPerAxis,
        [field: FieldOffset(76)] uint DynamicLevelCount,
        [field: FieldOffset(80)] uint HasStaticDirty,
        [field: FieldOffset(84)] uint EnableDynamic,
        [field: FieldOffset(88)] uint Reserved0,
        [field: FieldOffset(92)] uint Reserved1);

    [StructLayout(LayoutKind.Sequential, Size = 96)]
    private readonly record struct VoxelPrepareArgsRoot(
        GPUBufferView PoolState, GPUBufferView Counters,
        GPUBufferView ClearStaticArgs, GPUBufferView ClearDynamicArgs,
        GPUBufferView ExpandChunksArgs, GPUBufferView VoxelizeArgs,
        GPUBufferView ReclaimEmptyArgs, GPUBufferView InjectDynamicArgs,
        uint Mode, uint StaticCapacity, uint DynamicCapacity, uint MaxCandidates,
        uint MaxChunks, uint Reserved0, uint Reserved1, uint Reserved2);

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private readonly record struct VoxelExpandChunksRoot(
        GPUBufferView CandidateList, GPUBufferView ChunkList, GPUBufferView Counters,
        uint MaxCandidates, uint MaxChunks);

    [StructLayout(LayoutKind.Sequential, Size = 44)]
    private readonly record struct VoxelClearRoot(
        GPUBufferView ActiveBrickList, GPUBufferView BrickPool, GPUBufferView Counters,
        GPUTextureId VolumeTex, uint Mode, uint BrickLimitCounterOffset, uint BrickCapacity, uint VolumeResolution);

    [StructLayout(LayoutKind.Explicit, Size = 136)]
    private readonly record struct VoxelVoxelizeRoot(
        [field: FieldOffset(0)] World3DCell OriginAnchorCell,
        [field: FieldOffset(16)] GPUBufferView Directory,
        [field: FieldOffset(24)] GPUBufferView Cascades,
        [field: FieldOffset(32)] GPUBufferView StaticDirtyMask,
        [field: FieldOffset(40)] GPUBufferView StaticPageTable,
        [field: FieldOffset(48)] GPUBufferView StaticBrickPool,
        [field: FieldOffset(56)] GPUBufferView DynamicDesiredMask,
        [field: FieldOffset(64)] GPUBufferView DynamicPageTable,
        [field: FieldOffset(72)] GPUBufferView DynamicBrickPool,
        [field: FieldOffset(80)] GPUBufferView CandidateList,
        [field: FieldOffset(88)] GPUBufferView ChunkList,
        [field: FieldOffset(96)] GPUBufferView Counters,
        [field: FieldOffset(104)] uint BricksPerAxis,
        [field: FieldOffset(108)] uint StaticCapacity,
        [field: FieldOffset(112)] uint DynamicCapacity,
        [field: FieldOffset(116)] uint MaxChunks,
        [field: FieldOffset(120)] uint TargetSurfaceBucket,
        [field: FieldOffset(124)] uint EnableDynamic,
        [field: FieldOffset(128)] uint Reserved0,
        [field: FieldOffset(132)] uint Reserved1);

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private readonly record struct VoxelInjectRoot(
        GPUBufferView Frame, GPUBufferView Views, GPUBufferView ShadowFrame,
        GPUTextureId Vol0, GPUTextureId Vol1, GPUTextureId Vol2, GPUTextureId Vol3,
        uint DynamicMode, uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private readonly record struct VoxelMipRoot(
        GPUTextureId SrcTex, GPUTextureId DstTex, uint SrcMip, uint DstSize, uint Reserved0, uint Reserved1);

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private readonly record struct VoxelMipChainRoot(
        GPUTextureId SrcTex, GPUTextureId DstMip1, GPUTextureId DstMip2, GPUTextureId DstMip3,
        uint SrcMip, uint DstMip1Size, uint DstMip2Size, uint DstMip3Size);

    [StructLayout(LayoutKind.Sequential, Size = 88)]
    private readonly record struct VoxelPropagateRoot(
        GPUBufferView Cascades, GPUBufferView ReverseTable, GPUBufferView BrickPool,
        GPUBufferView PropBrickBuffer, GPUBufferView Counters,
        GPUTextureId Vol0, GPUTextureId Vol1, GPUTextureId Vol2, GPUTextureId Vol3,
        float PropagationDecay, uint Mode, uint DynamicMode, uint BricksPerAxis, uint PoolCapacity,
        uint Reserved0, uint Reserved1, uint Reserved2);

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private readonly record struct VoxelTraceRoot(
        GPUBufferView Frame, GPUBufferView Views, GPUBufferView ShadowFrame,
        GPUTextureId DepthTex, GPUTextureId NormalTex, GPUTextureId EmissiveTex, GPUTextureId MaterialTex,
        GPUTextureId Vol0, GPUTextureId Vol1, GPUTextureId Vol2, GPUTextureId Vol3,
        GPUTextureId OutDiffuseTex, GPUTextureId OutSpecularTex);

    [StructLayout(LayoutKind.Explicit, Size = 496)]
    private readonly record struct VoxelFrameGpu(
        [field: FieldOffset(0)] World3DCascadeGpu RsmCascade,
        [field: FieldOffset(96)] Matrix4x4 InvRsmLightViewProj,
        [field: FieldOffset(160)] Vector4 SunColor,
        [field: FieldOffset(176)] Vector4 SunDirIntensity,
        [field: FieldOffset(192)] Vector4 SkyHorizonAndAmbientFloor,
        [field: FieldOffset(208)] Vector4 SkyZenithAndFrameIndex,
        [field: FieldOffset(224)] uint Budget0,
        [field: FieldOffset(228)] uint Budget1,
        [field: FieldOffset(232)] uint Budget2,
        [field: FieldOffset(236)] uint Budget3,
        [field: FieldOffset(240)] GPUBufferView Cascades,
        [field: FieldOffset(248)] GPUBufferView EvictBoxes,
        [field: FieldOffset(256)] GPUBufferView PoolState,
        [field: FieldOffset(264)] GPUBufferView StaticFreeList,
        [field: FieldOffset(272)] GPUBufferView StaticPageTable,
        [field: FieldOffset(280)] GPUBufferView StaticReverseTable,
        [field: FieldOffset(288)] GPUBufferView StaticDirtyMask,
        [field: FieldOffset(296)] GPUBufferView DirtyQueue,
        [field: FieldOffset(304)] GPUBufferView ActiveDirtyList,
        [field: FieldOffset(312)] GPUBufferView DynamicDesiredMask,
        [field: FieldOffset(320)] GPUBufferView DynamicPageTable,
        [field: FieldOffset(328)] GPUBufferView DynamicReverseTable,
        [field: FieldOffset(336)] GPUBufferView DynamicActiveList,
        [field: FieldOffset(344)] GPUBufferView StaticBrickPool,
        [field: FieldOffset(352)] GPUBufferView DynamicBrickPool,
        [field: FieldOffset(360)] GPUBufferView Counters,
        [field: FieldOffset(368)] GPUBufferView PointLights,
        [field: FieldOffset(376)] GPUBufferView ReservedView0,
        [field: FieldOffset(384)] GPUTextureId BlueNoiseTex,
        [field: FieldOffset(388)] GPUTextureId RsmDepthTex,
        [field: FieldOffset(392)] GPUTextureId Vol0,
        [field: FieldOffset(396)] GPUTextureId Vol1,
        [field: FieldOffset(400)] GPUTextureId Vol2,
        [field: FieldOffset(404)] GPUTextureId Vol3,
        [field: FieldOffset(408)] uint BricksPerAxis,
        [field: FieldOffset(412)] uint StaticCapacity,
        [field: FieldOffset(416)] uint DynamicCapacity,
        [field: FieldOffset(420)] uint DynamicLevelCount,
        [field: FieldOffset(424)] uint EvictBoxCount,
        [field: FieldOffset(428)] uint StaticMaskWords,
        [field: FieldOffset(432)] uint DynamicMaskWords,
        [field: FieldOffset(436)] uint TotalStaticSlots,
        [field: FieldOffset(440)] uint TotalDynamicSlots,
        [field: FieldOffset(444)] uint PointLightCount,
        [field: FieldOffset(448)] float RsmDepthRange,
        [field: FieldOffset(452)] float EmissiveBoost,
        [field: FieldOffset(456)] float IndirectDiffuseIntensity,
        [field: FieldOffset(460)] float IndirectSpecularIntensity,
        [field: FieldOffset(464)] uint HasRsm,
        [field: FieldOffset(468)] uint HalfResMode,
        [field: FieldOffset(472)] uint TraceWidth,
        [field: FieldOffset(476)] uint TraceHeight,
        [field: FieldOffset(480)] uint FullWidth,
        [field: FieldOffset(484)] uint FullHeight,
        [field: FieldOffset(488)] uint Reserved0,
        [field: FieldOffset(492)] uint Reserved1);

    [StructLayout(LayoutKind.Sequential, Size = 80)]
    private readonly record struct VoxelDemosaicRoot(
        GPUBufferView Views, GPUTextureId DepthTex, GPUTextureId NormalTex,
        GPUTextureId RawDiffuseTex, GPUTextureId RawSpecularTex,
        GPUTextureId PrevHistoryDiffuseTex, GPUTextureId PrevHistorySpecularTex,
        GPUTextureId OutResolvedDiffuseTex, GPUTextureId OutResolvedSpecularTex,
        GPUTextureId OutHistoryDiffuseTex, GPUTextureId OutHistorySpecularTex,
        uint TraceWidth, uint TraceHeight, uint FullWidth, uint FullHeight,
        uint HalfResMode, uint FrameIndex, uint HasHistory, float TemporalBlendAlpha);

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private readonly record struct VoxelUpsampleRoot(
        GPUTextureId DepthTex, GPUTextureId NormalTex,
        GPUTextureId HalfDiffuseTex, GPUTextureId HalfSpecularTex,
        GPUTextureId OutDiffuseTex, GPUTextureId OutSpecularTex,
        uint FullWidth, uint FullHeight, uint HalfWidth, uint HalfHeight,
        uint FrameIndex, uint Reserved0);
}
