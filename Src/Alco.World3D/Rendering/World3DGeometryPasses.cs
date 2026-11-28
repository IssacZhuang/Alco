using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const uint IndexedArgsStride = 20;
    private const uint DrawRefStride = 8;
    private const uint CullViewEnabled = 1u;
    private const uint CullViewOrtho = 2u;
    private const uint CullViewShadowCasterOnly = 4u;
    private const uint CullViewHiZOcclusion = 8u;
    private const uint CullViewUseMaxDistance = 16u;
    private const uint CullViewIsPrimary = 32u;

    private readonly GBufferPrograms _gbufferPrograms;
    private readonly List<World3DView> _stagingViews = new(8);
    private readonly List<World3DCullViewGpu> _stagingCullViews = new(8);
    private readonly List<uint> _stagingTargetSlots = new(8);

    private BufferSlice _frameViews;
    private BufferSlice _frameCullViews;
    private BufferSlice _frameTargetSlots;
    private GpuBuffer? _selection;
    private GpuBuffer? _drawArgs;
    private GpuBuffer? _drawRefs;
    private GBufferTargets _gbufferTargets;
    private int _frameDrawSlots;
    private uint _frameCullViewCount;
    private bool _recordedWorld;
    private bool _submittedWorld;

    /// <summary>Synchronously counts nonempty GBuffer commands from the latest submitted world frame.</summary>
    /// <param name="count">Receives the number of commands that passed GPU culling across Phase 1 and Phase 2.</param>
    /// <returns>Whether a submitted GPU world frame was available to read.</returns>
    public bool TryReadVisibleDrawCount(out int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        count = 0;
        if (_context == null || _context.IsDisposed || _context.CurrentFrameId != 0 || _system.ActiveBackend == GraphicsBackend.NoGPU)
            return false;
        ReconcileFrame();
        if (!_submittedWorld)
            return false;
        if (LastRecordedDraws.IndirectSlots == 0)
            return true;

        byte[] bytes = GC.AllocateUninitializedArray<byte>(checked(_frameDrawSlots * (int)IndexedArgsStride));
        _system.ReadBuffer(_drawArgs!, bytes);
        foreach (ref readonly IndexedDrawArgs command in MemoryMarshal.Cast<byte, IndexedDrawArgs>(bytes))
        {
            if (command.InstanceCount != 0)
                ++count;
        }
        if (_submittedTwoPhaseOcclusion && _phase2DrawArgs != null)
        {
            _system.ReadBuffer(_phase2DrawArgs, bytes);
            foreach (ref readonly IndexedDrawArgs command in MemoryMarshal.Cast<byte, IndexedDrawArgs>(bytes))
            {
                if (command.InstanceCount != 0)
                    ++count;
            }
        }
        return true;
    }

    internal int LastCandidateWrites { get; private set; }

    internal BufferSlice GetDrawArgsGpuSlice() => _frameDrawSlots == 0
        ? default : _drawArgs!.Slice(0, (ulong)_frameDrawSlots * IndexedArgsStride);

    private void PrepareGeometryPasses(RenderContext context)
    {
        LastCandidateWrites = 0;
        _frameViews = default;
        _frameCullViews = default;
        _frameTargetSlots = default;
        _frameShadowData = default;
        _frameDrawSlots = 0;
        _frameCullViewCount = 0;
        if (!_recordedWorld)
            return;

        RefreshDrawLayout();
        LastCandidateWrites = _drawLayout.Stage(context, _system, _gpu);
        _frameDrawSlots = _drawLayout.SlotCount;

        _stagingViews.Clear();
        _stagingCullViews.Clear();
        _stagingTargetSlots.Clear();

        _stagingViews.Add(_recordedView);
        uint primaryFlags = CullViewEnabled | CullViewIsPrimary;
        if (_recordedOcclusionEnabled && _recordedHasPrevHiZ)
            primaryFlags |= CullViewHiZOcclusion;

        _stagingTargetSlots.Add(0);
        _stagingCullViews.Add(new World3DCullViewGpu(
            _recordedView.CameraVP,
            _recordedView.EyeCell,
            _recordedView.EyeLocalNear,
            0,
            primaryFlags,
            0f,
            0f,
            0f));

        PrepareShadows(context, in _recordedCamera, in _recordedView, _stagingViews, _stagingCullViews, _stagingTargetSlots);
        PrepareFarShadow(context, in _recordedView, _stagingViews, _stagingCullViews, _stagingTargetSlots);

        _frameCullViewCount = (uint)_stagingCullViews.Count;
        if (_recordedOcclusionEnabled && _recordedHasPrevHiZ)
        {
            _recordedPrevViewSlot = (uint)_stagingViews.Count;
            _stagingViews.Add(_submittedPrevView);
        }

        _frameViews = context.UploadTransient<World3DView>(CollectionsMarshal.AsSpan(_stagingViews));
        _frameCullViews = context.UploadTransient<World3DCullViewGpu>(CollectionsMarshal.AsSpan(_stagingCullViews));
        _frameTargetSlots = context.UploadTransient<uint>(CollectionsMarshal.AsSpan(_stagingTargetSlots));
        _frameShadowData = UploadShadowFrame(context);

        if (_frameDrawSlots == 0)
            return;

        uint activeViews = Math.Max(1u, _frameCullViewCount);
        EnsureDrawBuffer(ref _selection, (ulong)_instances.End * sizeof(uint) * activeViews);
        EnsureDrawBuffer(ref _drawArgs, (ulong)_frameDrawSlots * IndexedArgsStride * activeViews);
        EnsureDrawBuffer(ref _drawRefs, (ulong)_frameDrawSlots * DrawRefStride * activeViews);
    }

    private uint FrameViewCount => _frameCullViewCount;

    private void EnsureDrawBuffer(ref GpuBuffer? current, ulong bytes)
    {
        if (current != null && current.SizeBytes >= bytes)
            return;
        GpuBuffer next = _system.CreateBuffer(Math.Max(bytes, (current?.SizeBytes ?? 0) * 2), MemoryType.GpuOnly);
        current?.Dispose();
        current = next;
    }

    private uint SubmitShadowBucketDraws(RenderContext.RenderScope pass, uint viewSlot, bool rsm)
    {
        int packetLimit = _system.ActiveBackend is GraphicsBackend.Vulkan or GraphicsBackend.VulkanCompact
            ? (int)Math.Clamp(_system.Caps.MaxIndirectDrawCount, 1u, (uint)int.MaxValue) : 1;
        uint calls = 0;
        ulong baseCmd = (ulong)viewSlot * (ulong)_frameDrawSlots;
        foreach (DrawBucket bucket in _drawLayout.Buckets)
        {
            DrawRoute route = bucket.Key.Route;
            bool alphaTest = route.AlphaMode == World3DAlphaMode.Mask;
            foreach (DrawChunk chunk in bucket.Chunks)
            {
                for (int first = 0; first < chunk.Length;)
                {
                    int count = Math.Min(packetLimit, chunk.Length - first);
                    uint start = (uint)(chunk.First + first);
                    BufferSlice args = _drawArgs!.Slice((baseCmd + start) * IndexedArgsStride, (ulong)count * IndexedArgsStride);
                    if (rsm)
                    {
                        _gbufferPrograms.DrawRsm(pass, route.SurfaceModule, alphaTest, route.Cull,
                            _gpu.Directory!.View, _frameViews.View, _drawRefs!.View, bucket.Key.Page,
                            (uint)(baseCmd + start), args, (uint)count, viewSlot,
                            _recordedRsmSunDirIntensity, _recordedRsmSunColor);
                    }
                    else
                    {
                        _gbufferPrograms.DrawShadow(pass, route.SurfaceModule, alphaTest, route.Cull,
                            _gpu.Directory!.View, _frameViews.View, _drawRefs!.View, bucket.Key.Page,
                            (uint)(baseCmd + start), args, (uint)count, viewSlot);
                    }
                    ++calls;
                    first += count;
                }
            }
        }
        return calls;
    }

    private readonly record struct GBufferTargets(RGTexture Albedo, RGTexture Normal, RGTexture Material, RGTexture Emissive, RGTexture Depth);

    [StructLayout(LayoutKind.Sequential, Size = 100)]
    private readonly record struct CullSelectRoot(
        GPUBufferView Directory,
        GPUBufferView Views,
        GPUBufferView CullViews,
        GPUBufferView Selection,
        GPUBufferView Phase2Selection,
        GPUBufferView OccludedCandidates,
        GPUBufferView Counters,
        GPUTextureId PrevHiZTexture,
        uint InstanceCount,
        uint GroupsX,
        uint OcclusionFlags,
        uint PrevHiZViewportWidth,
        uint PrevHiZViewportHeight,
        uint PrevHiZMip0Width,
        uint PrevHiZMip0Height,
        uint PrevHiZMipCount,
        float MaximumDistance,
        uint PrevViewSlot);

    [StructLayout(LayoutKind.Sequential, Size = 72)]
    private readonly record struct BuildArgsRoot(
        GPUBufferView Directory,
        GPUBufferView Candidates,
        GPUBufferView Selection,
        GPUBufferView Args,
        GPUBufferView DrawRefs,
        GPUBufferView TargetViewSlots,
        GPUBufferView Phase2Args,
        uint InstanceCount,
        uint CandidateCount,
        uint GroupsX,
        uint Flags);

    [StructLayout(LayoutKind.Sequential, Size = (int)IndexedArgsStride)]
    private readonly record struct IndexedDrawArgs(uint IndexCount, uint InstanceCount, uint FirstIndex, int VertexOffset, uint FirstInstance);

    [StructLayout(LayoutKind.Sequential, Size = (int)DrawRefStride)]
    private readonly record struct DrawRef(uint InstanceSlot, uint Lod);

    private sealed class GeometryPasses(World3DScene scene) : IRenderGraphNode
    {
        private const int OcclusionClear = 0;
        private const int CullSelect = 1;
        private const int BuildArgs = 2;
        private const int OcclusionPrepareRetest = 3;
        private const int GBufferPhase1 = 4;
        private const int OcclusionRetest = 5;
        private const int OcclusionPreparePhase2 = 6;
        private const int BuildArgsPhase2 = 7;
        private const int GBufferPhase2 = 8;
        private const int HiZBuild1Base = 100;
        private const int HiZRefreshBase = 200;

        private readonly World3DScene _scene = scene;
        private Shader? _cullSelect;
        private Shader? _buildArgs;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _scene._gbufferTargets = default;
            _scene._frameCurrHiZ = default;
            _scene._frameCurrHiZTexture = null;
            if (!_scene._recordedWorld)
                return;

            bool draws = _scene._frameDrawSlots != 0;
            bool occEnabled = _scene._recordedOcclusionEnabled;
            bool hizBuildEnabled = _scene._recordedHiZBuildEnabled;
            bool twoPhase = _scene._recordedTwoPhaseOcclusion && draws;

            RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
            RGBuffer cullViews = default;
            RGBuffer targetSlots = default;
            RGBuffer args = default;
            RGBuffer drawRefs = default;
            RGBuffer candidates = default;
            RGBuffer counters = default;
            RGBuffer retestDispatch = default;
            RGBuffer phase2BuildDispatch = default;
            RGBuffer hizRefreshDispatches = default;
            RGBuffer occludedCandidates = default;
            RGBuffer phase2Selection = default;
            RGBuffer phase2Args = default;
            RGBuffer phase2Refs = default;
            RGBuffer selection = default;

            RGTexture currHiZ = default;
            RGTexture prevHiZ = default;
            if (hizBuildEnabled && _scene._recordedCurrHiZIndex >= 0)
            {
                Texture hizTex = _scene._hizTargets[_scene._recordedCurrHiZIndex]!;
                currHiZ = builder.ImportTexture(hizTex, "World3D.HiZCurr");
                _scene._frameCurrHiZ = currHiZ;
                _scene._frameCurrHiZTexture = hizTex;
                if (occEnabled && _scene._recordedHasPrevHiZ && _scene._recordedPrevHiZIndex >= 0)
                    prevHiZ = builder.ImportTexture(_scene._hizTargets[_scene._recordedPrevHiZIndex]!, "World3D.HiZPrev");
            }

            if (draws)
            {
                cullViews = builder.ImportBuffer(_scene._frameCullViews.Buffer!, "World3D.CullViews");
                targetSlots = builder.ImportBuffer(_scene._frameTargetSlots.Buffer!, "World3D.TargetSlots");
                candidates = builder.ImportBuffer(_scene._drawLayout.Buffer, "World3D.Candidates");
                selection = builder.ImportBuffer(_scene._selection!, "World3D.LodSelection");
                args = builder.ImportBuffer(_scene._drawArgs!, "World3D.IndexedArgs");
                drawRefs = builder.ImportBuffer(_scene._drawRefs!, "World3D.DrawRefs");

                if (twoPhase)
                {
                    counters = builder.ImportBuffer(_scene._occlusionCounters!, "World3D.OcclusionCounters");
                    retestDispatch = builder.ImportBuffer(_scene._retestDispatch!, "World3D.RetestDispatch");
                    phase2BuildDispatch = builder.ImportBuffer(_scene._phase2BuildDispatch!, "World3D.Phase2BuildDispatch");
                    hizRefreshDispatches = builder.ImportBuffer(_scene._hizRefreshDispatches!, "World3D.HiZRefreshDispatches");
                    occludedCandidates = builder.ImportBuffer(_scene._occludedCandidates!, "World3D.OccludedCandidates");
                    phase2Selection = builder.ImportBuffer(_scene._phase2Selection!, "World3D.Phase2Selection");
                    phase2Args = builder.ImportBuffer(_scene._phase2DrawArgs!, "World3D.Phase2IndexedArgs");
                    phase2Refs = builder.ImportBuffer(_scene._phase2DrawRefs!, "World3D.Phase2DrawRefs");

                    RenderGraphPassBuilder clearPass = builder.AddPass("World3D.Occlusion.Clear", OcclusionClear);
                    clearPass.Write(counters, RGUsage.ComputeWrite);
                    clearPass.Write(retestDispatch, RGUsage.ComputeWrite);
                    clearPass.Write(phase2BuildDispatch, RGUsage.ComputeWrite);
                    clearPass.Write(hizRefreshDispatches, RGUsage.ComputeWrite);
                }

                RenderGraphPassBuilder cull = builder.AddPass("World3D.CullSelect", CullSelect);
                cull.Read(views, RGUsage.ComputeRead);
                cull.Read(cullViews, RGUsage.ComputeRead);
                _scene._gpu.ReadTables(builder, cull, RGUsage.ComputeRead);
                cull.Write(selection, RGUsage.ComputeWrite);
                if (prevHiZ.IsValid)
                    cull.Read(prevHiZ, RGUsage.ComputeRead);
                if (twoPhase)
                {
                    cull.ReadWrite(counters, RGUsage.ComputeReadWrite);
                    cull.Write(phase2Selection, RGUsage.ComputeWrite);
                    cull.Write(occludedCandidates, RGUsage.ComputeWrite);
                }

                RenderGraphPassBuilder emit = builder.AddPass("World3D.BuildArgs", BuildArgs);
                _scene._gpu.ReadTables(builder, emit, RGUsage.ComputeRead);
                emit.Read(candidates, RGUsage.ComputeRead);
                emit.Read(targetSlots, RGUsage.ComputeRead);
                emit.Read(selection, RGUsage.ComputeRead);
                emit.Write(args, RGUsage.ComputeWrite);
                emit.Write(drawRefs, RGUsage.ComputeWrite);
                if (twoPhase)
                    emit.Write(phase2Args, RGUsage.ComputeWrite);

                if (twoPhase)
                {
                    RenderGraphPassBuilder prep1 = builder.AddPass("World3D.Occlusion.PrepareRetest", OcclusionPrepareRetest);
                    prep1.Read(counters, RGUsage.ComputeRead);
                    prep1.Write(retestDispatch, RGUsage.ComputeWrite);
                }
            }

            GBufferTargets targets = new(
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Albedo", Format = Format.Rgba8Unorm }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Normal", Format = Format.Rgba16Float }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Material", Format = Format.Rgba8Unorm }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Emissive", Format = Format.Rgba16Float }),
                builder.CreateTexture(new RGTextureDesc { Name = "World3D.Depth", Format = Format.D32Float }));
            _scene._gbufferTargets = targets;

            RenderGraphPassBuilder gbuffer = builder.AddPass("World3D.GBuffer", GBufferPhase1);
            gbuffer.Write(targets.Albedo, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Normal, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Material, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Emissive, RGUsage.ColorAttachmentWrite);
            gbuffer.Write(targets.Depth, RGUsage.DepthStencil);
            if (draws)
            {
                gbuffer.Read(views, RGUsage.VertexRead);
                gbuffer.Read(args, RGUsage.IndirectRead);
                gbuffer.Read(drawRefs, RGUsage.VertexRead);
                _scene._gpu.ReadTables(builder, gbuffer, RGUsage.VertexRead | RGUsage.PixelRead, materials: true);
                foreach (DrawBucket bucket in _scene._drawLayout.Buckets)
                    gbuffer.Read(builder.ImportBuffer(bucket.Key.Page, "World3D.GeometryPayload"), RGUsage.IndexRead | RGUsage.VertexRead);
            }

            if (hizBuildEnabled && currHiZ.IsValid)
            {
                uint mips = _scene._recordedHiZMipCount;
                for (uint mip = 0; mip < mips; ++mip)
                {
                    RenderGraphPassBuilder hizPass = builder.AddPass($"World3D.HiZ.Build1.Mip{mip}", HiZBuild1Base + (int)mip);
                    if (mip == 0)
                    {
                        hizPass.Read(targets.Depth, RGUsage.ComputeRead);
                        hizPass.Write(currHiZ, RGUsage.ComputeWrite);
                    }
                    else
                    {
                        hizPass.ReadWrite(currHiZ, RGUsage.ComputeReadWrite);
                    }
                    if (occEnabled && !twoPhase && mip == mips - 1)
                        hizPass.ProducesOutput();
                }

                if (twoPhase)
                {
                    RenderGraphPassBuilder retest = builder.AddPass("World3D.Occlusion.Retest", OcclusionRetest);
                    retest.Read(retestDispatch, RGUsage.IndirectRead);
                    retest.Read(views, RGUsage.ComputeRead);
                    _scene._gpu.ReadTables(builder, retest, RGUsage.ComputeRead);
                    retest.Read(occludedCandidates, RGUsage.ComputeRead);
                    retest.Read(currHiZ, RGUsage.ComputeRead);
                    retest.ReadWrite(counters, RGUsage.ComputeReadWrite);
                    retest.ReadWrite(phase2Selection, RGUsage.ComputeReadWrite);

                    RenderGraphPassBuilder prep2 = builder.AddPass("World3D.Occlusion.PreparePhase2", OcclusionPreparePhase2);
                    prep2.Read(counters, RGUsage.ComputeRead);
                    prep2.Write(phase2BuildDispatch, RGUsage.ComputeWrite);
                    prep2.Write(hizRefreshDispatches, RGUsage.ComputeWrite);

                    RenderGraphPassBuilder build2 = builder.AddPass("World3D.BuildArgs.Phase2", BuildArgsPhase2);
                    build2.Read(phase2BuildDispatch, RGUsage.IndirectRead);
                    _scene._gpu.ReadTables(builder, build2, RGUsage.ComputeRead);
                    build2.Read(candidates, RGUsage.ComputeRead);
                    build2.Read(phase2Selection, RGUsage.ComputeRead);
                    build2.ReadWrite(phase2Args, RGUsage.ComputeReadWrite);
                    build2.ReadWrite(phase2Refs, RGUsage.ComputeReadWrite);

                    RenderGraphPassBuilder gbuffer2 = builder.AddPass("World3D.GBuffer.Phase2", GBufferPhase2);
                    gbuffer2.ReadWrite(targets.Albedo, RGUsage.ColorAttachment);
                    gbuffer2.ReadWrite(targets.Normal, RGUsage.ColorAttachment);
                    gbuffer2.ReadWrite(targets.Material, RGUsage.ColorAttachment);
                    gbuffer2.ReadWrite(targets.Emissive, RGUsage.ColorAttachment);
                    gbuffer2.ReadWrite(targets.Depth, RGUsage.DepthStencil);
                    gbuffer2.Read(views, RGUsage.VertexRead);
                    gbuffer2.Read(phase2Args, RGUsage.IndirectRead);
                    gbuffer2.Read(phase2Refs, RGUsage.VertexRead);
                    _scene._gpu.ReadTables(builder, gbuffer2, RGUsage.VertexRead | RGUsage.PixelRead, materials: true);
                    foreach (DrawBucket bucket in _scene._drawLayout.Buckets)
                        gbuffer2.Read(builder.ImportBuffer(bucket.Key.Page, "World3D.GeometryPayload"), RGUsage.IndexRead | RGUsage.VertexRead);

                    for (uint mip = 0; mip < mips; ++mip)
                    {
                        RenderGraphPassBuilder hizRefresh = builder.AddPass($"World3D.HiZ.Refresh.Mip{mip}", HiZRefreshBase + (int)mip);
                        hizRefresh.Read(hizRefreshDispatches, RGUsage.IndirectRead);
                        if (mip == 0)
                            hizRefresh.Read(targets.Depth, RGUsage.ComputeRead);
                        hizRefresh.ReadWrite(currHiZ, RGUsage.ComputeReadWrite);
                        if (mip == mips - 1)
                            hizRefresh.ProducesOutput();
                    }
                }
            }
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            if (passId == OcclusionClear)
            {
                _scene.DispatchOcclusionPrepare(context.RenderContext, 0);
                return;
            }
            if (passId == CullSelect)
            {
                DispatchCull(context.RenderContext);
                return;
            }
            if (passId == BuildArgs)
            {
                DispatchBuildArgs(context.RenderContext, phase2: false);
                return;
            }
            if (passId == OcclusionPrepareRetest)
            {
                _scene.DispatchOcclusionPrepare(context.RenderContext, 1);
                return;
            }
            if (passId == GBufferPhase1)
            {
                RenderGBuffer(in context, phase2: false);
                return;
            }
            if (passId == OcclusionRetest)
            {
                _scene.DispatchOcclusionRetest(context.RenderContext);
                return;
            }
            if (passId == OcclusionPreparePhase2)
            {
                _scene.DispatchOcclusionPrepare(context.RenderContext, 2);
                return;
            }
            if (passId == BuildArgsPhase2)
            {
                DispatchBuildArgs(context.RenderContext, phase2: true);
                return;
            }
            if (passId == GBufferPhase2)
            {
                RenderGBuffer(in context, phase2: true);
                return;
            }
            if (passId >= HiZRefreshBase)
            {
                Texture depth = context.GetTexture(_scene._gbufferTargets.Depth);
                _scene.DispatchHiZMip(context.RenderContext, depth, (uint)(passId - HiZRefreshBase), indirect: true);
                return;
            }
            if (passId >= HiZBuild1Base)
            {
                Texture depth = context.GetTexture(_scene._gbufferTargets.Depth);
                _scene.DispatchHiZMip(context.RenderContext, depth, (uint)(passId - HiZBuild1Base), indirect: false);
            }
        }

        private void DispatchCull(RenderContext context)
        {
            _cullSelect ??= LoadCompute<CullSelectRoot>("World3DCullSelect");
            uint count = (uint)_scene._instances.End;
            Extent3D groups = Groups(count, _scene.FrameViewCount);
            uint occFlags = 0;
            if (_scene._recordedOcclusionEnabled && _scene._recordedHasPrevHiZ) occFlags |= 1u;
            if (_scene._recordedTwoPhaseOcclusion) occFlags |= 2u;
            if (_scene._recordedHasPrevHiZ) occFlags |= 4u;

            GPUTextureId prevHiZId = _scene._recordedHasPrevHiZ && _scene._recordedPrevHiZIndex >= 0
                ? GPUTextureId.FromSampled(_scene._hizTargets[_scene._recordedPrevHiZIndex]!)
                : default;

            CullSelectRoot root = new(
                _scene._gpu.Directory!.View,
                _scene._frameViews.View,
                _scene._frameCullViews.View,
                _scene._selection!.View,
                _scene._recordedTwoPhaseOcclusion ? _scene._phase2Selection!.View : default,
                _scene._recordedTwoPhaseOcclusion ? _scene._occludedCandidates!.View : default,
                _scene._recordedTwoPhaseOcclusion ? _scene._occlusionCounters!.View : default,
                prevHiZId,
                count,
                groups.X,
                occFlags,
                _scene._submittedHiZViewport.X,
                _scene._submittedHiZViewport.Y,
                _scene._submittedHiZMip0.X,
                _scene._submittedHiZMip0.Y,
                _scene._submittedHiZMipCount,
                _scene.MaximumDrawDistance,
                _scene._recordedPrevViewSlot);
            BufferSlice data = context.UploadTransient(in root);
            using RenderContext.ComputeScope compute = context.BeginCompute();
            compute.Dispatch(_cullSelect, data, groups);
        }

        private void DispatchBuildArgs(RenderContext context, bool phase2)
        {
            _buildArgs ??= LoadCompute<BuildArgsRoot>("World3DBuildArgs");
            uint count = (uint)_scene._frameDrawSlots;
            Extent3D groups = Groups(count, phase2 ? 1u : _scene.FrameViewCount);
            uint flags = phase2 ? 1u : (_scene._recordedTwoPhaseOcclusion ? 2u : 0u);
            BuildArgsRoot root = new(
                _scene._gpu.Directory!.View,
                _scene._drawLayout.Buffer.View,
                phase2 ? _scene._phase2Selection!.View : _scene._selection!.View,
                phase2 ? _scene._phase2DrawArgs!.View : _scene._drawArgs!.View,
                phase2 ? _scene._phase2DrawRefs!.View : _scene._drawRefs!.View,
                _scene._frameTargetSlots.View,
                _scene._recordedTwoPhaseOcclusion ? _scene._phase2DrawArgs!.View : default,
                (uint)_scene._instances.End,
                count,
                groups.X,
                flags);
            BufferSlice data = context.UploadTransient(in root);
            using RenderContext.ComputeScope compute = context.BeginCompute();
            if (phase2)
                compute.DispatchIndirect(_buildArgs, data, _scene._phase2BuildDispatch!.Slice(0, 12));
            else
                compute.Dispatch(_buildArgs, data, groups);
        }

        private Shader LoadCompute<T>(string name) where T : unmanaged
        {
            ShaderSystem shaders = _scene._system.Shaders;
            IReadOnlyList<ShaderTypeLayout> types = shaders.Compiler.LoadModuleFromSource(name, name + ".slang", ReadWorldShader(name)).GetTypes();
            if (typeof(T) == typeof(BuildArgsRoot))
            {
                GpuStore.ValidateLayout<DrawCandidate>(RequireType(types, nameof(DrawCandidate)));
                GpuStore.ValidateLayout<IndexedDrawArgs>(RequireType(types, nameof(IndexedDrawArgs)));
                GpuStore.ValidateLayout<DrawRef>(RequireType(types, nameof(DrawRef)));
            }
            Shader shader = shaders.GetShader(name, ShaderKind.Compute);
            ValidateRoot<T>(shader.Reflection.Root ?? throw new InvalidOperationException($"{name} has no GPU root."));
            return shader;
        }

        private static ShaderTypeLayout RequireType(IReadOnlyList<ShaderTypeLayout> types, string name) =>
            types.FirstOrDefault(t => t.TypeName == name) ?? throw new InvalidOperationException($"Missing shader ABI type {name}.");

        private static Extent3D Groups(uint count, uint viewCount)
        {
            uint groups = (count + 63) / 64;
            uint x = Math.Min(65535u, groups);
            return new Extent3D(x, (groups + x - 1) / x, viewCount);
        }

        private void RenderGBuffer(in RenderGraphContext context, bool phase2)
        {
            GBufferTargets targets = _scene._gbufferTargets;
            LoadOp loadOp = phase2 ? LoadOp.Load : LoadOp.Clear;
            RenderingDesc attachments = new()
            {
                Colors =
                [
                    Attachment(context.GetRenderTarget(targets.Albedo), loadOp),
                    Attachment(context.GetRenderTarget(targets.Normal), loadOp),
                    Attachment(context.GetRenderTarget(targets.Material), loadOp),
                    Attachment(context.GetRenderTarget(targets.Emissive), loadOp),
                ],
                Depth = new DepthAttachment { Target = context.GetRenderTarget(targets.Depth), Load = loadOp, Store = StoreOp.Store, Clear = 0 },
            };
            using RenderContext.RenderScope pass = context.RenderContext.BeginRenderPass(attachments);
            if (_scene._frameDrawSlots == 0)
                return;

            GpuBuffer argsBuffer = phase2 ? _scene._phase2DrawArgs! : _scene._drawArgs!;
            GpuBuffer refsBuffer = phase2 ? _scene._phase2DrawRefs! : _scene._drawRefs!;
            int packetLimit = _scene._system.ActiveBackend is GraphicsBackend.Vulkan or GraphicsBackend.VulkanCompact
                ? (int)Math.Clamp(_scene._system.Caps.MaxIndirectDrawCount, 1u, (uint)int.MaxValue) : 1;
            int calls = 0;
            int slots = 0;
            foreach (DrawBucket bucket in _scene._drawLayout.Buckets)
            {
                DrawRoute route = bucket.Key.Route;
                foreach (DrawChunk chunk in bucket.Chunks)
                {
                    slots += chunk.Length;
                    for (int first = 0; first < chunk.Length;)
                    {
                        int count = Math.Min(packetLimit, chunk.Length - first);
                        uint start = (uint)(chunk.First + first);
                        BufferSlice args = argsBuffer.Slice((ulong)start * IndexedArgsStride, (ulong)count * IndexedArgsStride);
                        _scene._gbufferPrograms.Draw(pass, route.SurfaceModule, route.AlphaMode == World3DAlphaMode.Mask, route.Cull,
                            _scene._gpu.Directory!.View, _scene._frameViews.View, refsBuffer.View, bucket.Key.Page, start, args, (uint)count);
                        ++calls;
                        first += count;
                    }
                }
            }
            if (!phase2)
                _scene.LastRecordedDraws = new World3DDrawStats(calls, slots);
            else
                _scene.LastRecordedDraws = new World3DDrawStats(_scene.LastRecordedDraws.MdiCalls + calls, _scene.LastRecordedDraws.IndirectSlots);
        }

        private static ColorAttachment Attachment(RenderTarget target, LoadOp loadOp) =>
            new() { Target = target, Load = loadOp, Store = StoreOp.Store };
    }
}
