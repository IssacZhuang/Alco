using System.Runtime.InteropServices;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const int MaxHiZMips = 16;
    private const ulong OcclusionCounterBytes = 16;
    private const ulong IndirectDispatchStride = 16;

    private readonly Texture?[] _hizTargets = new Texture?[2];
    private int _submittedHiZIndex = -1;
    private Extent2D _submittedHiZViewport;
    private Extent2D _submittedHiZMip0;
    private uint _submittedHiZMipCount;
    private World3DView _submittedPrevView;
    private ulong _submittedOcclusionDiscardVersion;
    private bool _submittedTwoPhaseOcclusion;

    private int _recordedCurrHiZIndex = -1;
    private int _recordedPrevHiZIndex = -1;
    private Extent2D _recordedHiZViewport;
    private Extent2D _recordedHiZMip0;
    private uint _recordedHiZMipCount;
    private bool _recordedOcclusionEnabled;
    private bool _recordedHiZBuildEnabled;
    private bool _recordedTwoPhaseOcclusion;
    private bool _recordedHasPrevHiZ;
    private uint _recordedPrevViewSlot;

    private GpuBuffer? _occlusionCounters;
    private GpuBuffer? _retestDispatch;
    private GpuBuffer? _phase2BuildDispatch;
    private GpuBuffer? _hizRefreshDispatches;
    private GpuBuffer? _occludedCandidates;
    private GpuBuffer? _phase2Selection;
    private GpuBuffer? _phase2DrawArgs;
    private GpuBuffer? _phase2DrawRefs;

    private Shader? _hizBuildShader;
    private Shader? _occlusionPrepareShader;
    private Shader? _occlusionRetestShader;

    private uint _cachedReadbackPhase1Visible;
    private uint _cachedReadbackOccluded;
    private uint _cachedReadbackPhase2Visible;
    private bool _hasReadbackOcclusionCounts;

    private RGTexture _frameCurrHiZ;
    private Texture? _frameCurrHiZTexture;

    /// <summary>Synchronously reads GPU occlusion counters from the latest submitted world frame.</summary>
    /// <param name="phase1Visible">Number of instances passing Phase 1 visibility.</param>
    /// <param name="occludedCandidates">Number of frustum-visible instances queued for Phase 2 retest.</param>
    /// <param name="phase2Visible">Number of previously occluded instances resurrected in Phase 2.</param>
    /// <returns>True when a submitted GPU world frame with two-phase counters was available to read.</returns>
    public bool TryReadOcclusionCounts(out uint phase1Visible, out uint occludedCandidates, out uint phase2Visible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        phase1Visible = 0;
        occludedCandidates = 0;
        phase2Visible = 0;
        if (_context == null || _context.IsDisposed || _context.CurrentFrameId != 0 || _system.ActiveBackend == GraphicsBackend.NoGPU)
            return false;
        ReconcileFrame();
        if (!_submittedWorld || _occlusionCounters == null)
            return false;

        Span<byte> bytes = stackalloc byte[(int)OcclusionCounterBytes];
        _system.ReadBuffer(_occlusionCounters, bytes);
        ReadOnlySpan<uint> counters = MemoryMarshal.Cast<byte, uint>(bytes);
        occludedCandidates = counters[0];
        phase1Visible = counters[1];
        phase2Visible = counters[2];
        _cachedReadbackPhase1Visible = phase1Visible;
        _cachedReadbackOccluded = occludedCandidates;
        _cachedReadbackPhase2Visible = phase2Visible;
        _hasReadbackOcclusionCounts = true;
        LastRecordedOcclusion = new World3DOcclusionStats(
            Occlusion.Enabled,
            Occlusion.TwoPhase,
            _submittedHiZMip0,
            _submittedHiZMipCount,
            true,
            phase1Visible,
            occludedCandidates,
            phase2Visible);
        return true;
    }

    private void PrepareOcclusion(RenderContext context, Extent2D viewport)
    {
        _recordedCurrHiZIndex = -1;
        _recordedPrevHiZIndex = -1;
        _recordedHiZViewport = default;
        _recordedHiZMip0 = default;
        _recordedHiZMipCount = 0;
        _recordedOcclusionEnabled = _recordedWorld && Occlusion.Enabled && viewport.X > 0 && viewport.Y > 0;
        _recordedHiZBuildEnabled = _recordedWorld && (Occlusion.Enabled || Ssr.Enabled || Forward.Enabled || LightingDebugView == World3DLightingDebugView.HiZMip0) && viewport.X > 0 && viewport.Y > 0;
        _recordedTwoPhaseOcclusion = false;
        _recordedHasPrevHiZ = false;
        _recordedPrevViewSlot = 0;
        _frameCurrHiZ = default;
        _frameCurrHiZTexture = null;

        if (!_recordedHiZBuildEnabled)
        {
            LastRecordedOcclusion = new World3DOcclusionStats(false, Occlusion.TwoPhase, default, 0,
                _hasReadbackOcclusionCounts, _cachedReadbackPhase1Visible, _cachedReadbackOccluded, _cachedReadbackPhase2Visible);
            return;
        }

        Extent2D mip0 = new(Math.Max(1u, viewport.X >> 1), Math.Max(1u, viewport.Y >> 1));
        uint mipCount = Math.Min(ComputeMipCount(mip0), MaxHiZMips);
        _recordedHiZViewport = viewport;
        _recordedHiZMip0 = mip0;
        _recordedHiZMipCount = mipCount;

        bool prevValid = _recordedOcclusionEnabled
            && _submittedHiZIndex >= 0
            && _submittedOcclusionDiscardVersion == context.DiscardVersion
            && _submittedHiZViewport.Equals(viewport)
            && _hizTargets[_submittedHiZIndex] is Texture prevTex
            && !prevTex.IsDisposed
            && prevTex.Extent.X == mip0.X
            && prevTex.Extent.Y == mip0.Y
            && prevTex.MipLevels == mipCount;

        int writeIndex = prevValid ? 1 - _submittedHiZIndex : 0;
        EnsureHiZTexture(writeIndex, mip0, mipCount);
        _frameCurrHiZTexture = _hizTargets[writeIndex];
        _recordedCurrHiZIndex = writeIndex;
        _recordedPrevHiZIndex = prevValid ? _submittedHiZIndex : -1;
        _recordedHasPrevHiZ = prevValid;
        _recordedTwoPhaseOcclusion = _recordedOcclusionEnabled && Occlusion.TwoPhase && prevValid;

        if (_recordedOcclusionEnabled && _frameDrawSlots != 0 && _instances.End > 0)
        {
            EnsureDrawBuffer(ref _occlusionCounters, OcclusionCounterBytes);
            EnsureDrawBuffer(ref _retestDispatch, IndirectDispatchStride);
            EnsureDrawBuffer(ref _phase2BuildDispatch, IndirectDispatchStride);
            EnsureDrawBuffer(ref _hizRefreshDispatches, MaxHiZMips * IndirectDispatchStride);
            EnsureDrawBuffer(ref _occludedCandidates, (ulong)_instances.End * (ulong)Marshal.SizeOf<World3DOccludedCandidate>());
            EnsureDrawBuffer(ref _phase2Selection, (ulong)_instances.End * sizeof(uint));
            EnsureDrawBuffer(ref _phase2DrawArgs, (ulong)_frameDrawSlots * IndexedArgsStride);
            EnsureDrawBuffer(ref _phase2DrawRefs, (ulong)_frameDrawSlots * DrawRefStride);
        }

        LastRecordedOcclusion = new World3DOcclusionStats(
            _recordedOcclusionEnabled,
            Occlusion.TwoPhase,
            _recordedOcclusionEnabled ? mip0 : default,
            _recordedOcclusionEnabled ? mipCount : 0,
            _hasReadbackOcclusionCounts,
            _cachedReadbackPhase1Visible,
            _cachedReadbackOccluded,
            _cachedReadbackPhase2Visible);
    }

    private void ReconcileOcclusion(bool submitted)
    {
        _submittedTwoPhaseOcclusion = submitted && _recordedWorld && _recordedTwoPhaseOcclusion;
        if (submitted && _recordedWorld && _recordedHiZBuildEnabled && _recordedCurrHiZIndex >= 0)
        {
            _submittedHiZIndex = _recordedCurrHiZIndex;
            _submittedHiZViewport = _recordedHiZViewport;
            _submittedHiZMip0 = _recordedHiZMip0;
            _submittedHiZMipCount = _recordedHiZMipCount;
            _submittedPrevView = _recordedView;
            _submittedOcclusionDiscardVersion = _recordedDiscardVersion;
        }
        else if (!submitted || !_recordedHiZBuildEnabled || !_recordedWorld)
        {
            _submittedHiZIndex = -1;
        }
    }

    private void DisposeOcclusionResources()
    {
        for (int i = 0; i < _hizTargets.Length; ++i)
        {
            _hizTargets[i]?.Dispose();
            _hizTargets[i] = null;
        }
        _submittedHiZIndex = -1;
        _occlusionCounters?.Dispose();
        _occlusionCounters = null;
        _retestDispatch?.Dispose();
        _retestDispatch = null;
        _phase2BuildDispatch?.Dispose();
        _phase2BuildDispatch = null;
        _hizRefreshDispatches?.Dispose();
        _hizRefreshDispatches = null;
        _occludedCandidates?.Dispose();
        _occludedCandidates = null;
    }

    private void EnsureHiZTexture(int index, Extent2D mip0, uint mipCount)
    {
        Texture? existing = _hizTargets[index];
        if (existing != null && existing.Extent.X == mip0.X && existing.Extent.Y == mip0.Y && existing.MipLevels == mipCount)
            return;
        existing?.Dispose();
        _hizTargets[index] = _system.CreateTexture(new TextureDesc
        {
            Type = TextureType.TwoD,
            Extent = new Extent3D(mip0.X, mip0.Y, 1),
            MipLevels = mipCount,
            LayerCount = 1,
            Format = Format.Rg32Float,
            Usage = TextureUsage.Sampled | TextureUsage.Storage,
        });
    }

    private void DispatchOcclusionPrepare(RenderContext context, uint mode)
    {
        _occlusionPrepareShader ??= LoadSceneCompute<OcclusionPrepareRoot>("World3DOcclusionPrepare");
        Extent2D candGroups = DispatchGrid2D((uint)_frameDrawSlots);
        OcclusionPrepareRoot root = new(
            _occlusionCounters!.View,
            _retestDispatch!.View,
            _phase2BuildDispatch!.View,
            _hizRefreshDispatches!.View,
            mode,
            candGroups.X,
            candGroups.Y,
            _recordedHiZMip0.X,
            _recordedHiZMip0.Y,
            _recordedHiZMipCount,
            (uint)_instances.End,
            0);
        BufferSlice data = context.UploadTransient(in root);
        using RenderContext.ComputeScope compute = context.BeginCompute();
        compute.Dispatch(_occlusionPrepareShader, data, new Extent3D(1, 1, 1));
    }

    private void DispatchOcclusionRetest(RenderContext context)
    {
        _occlusionRetestShader ??= LoadSceneCompute<OcclusionRetestRoot>("World3DOcclusionRetest");
        OcclusionRetestRoot root = new(
            _gpu.Directory!.View,
            _frameViews.View,
            _occlusionCounters!.View,
            _occludedCandidates!.View,
            _phase2Selection!.View,
            GPUTextureId.FromSampled(_hizTargets[_recordedCurrHiZIndex]!),
            _recordedHiZMip0.X,
            _recordedHiZMip0.Y,
            _recordedHiZMipCount,
            0);
        BufferSlice data = context.UploadTransient(in root);
        using RenderContext.ComputeScope compute = context.BeginCompute();
        compute.DispatchIndirect(_occlusionRetestShader, data, _retestDispatch!.Slice(0, 12));
    }

    private void DispatchHiZMip(RenderContext context, Texture depthTexture, uint mipLevel, bool indirect)
    {
        _hizBuildShader ??= LoadSceneCompute<HiZBuildRoot>("World3DHiZBuild");
        Texture hiz = _hizTargets[_recordedCurrHiZIndex]!;
        uint srcW = mipLevel == 0 ? _recordedHiZViewport.X : Math.Max(1u, _recordedHiZMip0.X >> (int)(mipLevel - 1));
        uint srcH = mipLevel == 0 ? _recordedHiZViewport.Y : Math.Max(1u, _recordedHiZMip0.Y >> (int)(mipLevel - 1));
        uint dstW = Math.Max(1u, _recordedHiZMip0.X >> (int)mipLevel);
        uint dstH = Math.Max(1u, _recordedHiZMip0.Y >> (int)mipLevel);

        HiZBuildRoot root = new(
            mipLevel == 0 ? GPUTextureId.FromSampled(depthTexture) : GPUTextureId.FromSampled(hiz),
            hiz.GetStorageId(mipLevel),
            srcW,
            srcH,
            dstW,
            dstH,
            mipLevel,
            0);
        BufferSlice data = context.UploadTransient(in root);
        using RenderContext.ComputeScope compute = context.BeginCompute();
        if (indirect)
            compute.DispatchIndirect(_hizBuildShader, data, _hizRefreshDispatches!.Slice(mipLevel * IndirectDispatchStride, 12));
        else
            compute.Dispatch(_hizBuildShader, data, new Extent3D((dstW + 7) / 8, (dstH + 7) / 8, 1));
    }

    private static uint ComputeMipCount(Extent2D extent)
    {
        uint maxDim = Math.Max(extent.X, extent.Y);
        uint mips = 1;
        while (maxDim > 1)
        {
            maxDim >>= 1;
            ++mips;
        }
        return mips;
    }

    private static Extent2D DispatchGrid2D(uint itemCount)
    {
        if (itemCount == 0)
            return new Extent2D(0, 0);
        uint groups = (itemCount + 63) / 64;
        uint x = Math.Min(65535u, groups);
        return new Extent2D(x, (groups + x - 1) / x);
    }

    private Shader LoadSceneCompute<T>(string name) where T : unmanaged
    {
        ShaderSystem shaders = _system.Shaders;
        shaders.Compiler.LoadModuleFromSource(name, name + ".slang", ReadWorldShader(name));
        Shader shader = shaders.GetShader(name, ShaderKind.Compute);
        ValidateRoot<T>(shader.Reflection.Root ?? throw new InvalidOperationException($"{name} has no GPU root."));
        return shader;
    }

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private readonly record struct HiZBuildRoot(
        GPUTextureId SrcTexture,
        GPUTextureId DstTexture,
        uint SrcWidth,
        uint SrcHeight,
        uint DstWidth,
        uint DstHeight,
        uint MipLevel,
        uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private readonly record struct OcclusionPrepareRoot(
        GPUBufferView Counters,
        GPUBufferView RetestDispatch,
        GPUBufferView Phase2BuildDispatch,
        GPUBufferView HiZRefreshDispatches,
        uint Mode,
        uint CandGroupsX,
        uint CandGroupsY,
        uint HiZMip0Width,
        uint HiZMip0Height,
        uint HiZMipCount,
        uint MaxInstances,
        uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 60)]
    private readonly record struct OcclusionRetestRoot(
        GPUBufferView Directory,
        GPUBufferView Views,
        GPUBufferView Counters,
        GPUBufferView OccludedCandidates,
        GPUBufferView Phase2Selection,
        GPUTextureId HiZTexture,
        uint HiZMip0Width,
        uint HiZMip0Height,
        uint HiZMipCount,
        uint Reserved0);
}
