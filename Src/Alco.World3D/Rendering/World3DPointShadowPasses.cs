using System.Runtime.InteropServices;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private readonly Texture?[] _pointShadowTextures = new Texture?[PointShadowTiers.Length];
    private readonly RGTexture[] _framePointShadowTiers = new RGTexture[PointShadowTiers.Length];
    private readonly List<PointShadowFaceUpdate> _pointShadowFaceUpdates = [];
    private readonly List<PointShadowBatch> _pointShadowBatches = [];
    private readonly List<PointShadowCandidateGpu> _pointShadowCandidates = [];
    private GpuBuffer? _pointShadowArgs;
    private GpuBuffer? _pointShadowDrawRefs;
    private GpuBuffer? _pointShadowCounters;
    private Shader? _pointShadowCullShader;
    private BufferSlice _pointShadowFramePages;
    private BufferSlice _pointShadowFrameViews;
    private BufferSlice _pointShadowFrameCandidates;
    private BufferSlice _pointShadowFrameBatches;
    private uint _pointShadowSampledTiers;

    private void ResetPointShadowRecording()
    {
        LastRecordedPointShadows = default;
        _pointShadowFaceUpdates.Clear();
        _pointShadowBatches.Clear();
        _pointShadowCandidates.Clear();
        _pointShadowLocalBatches.Clear();
        _pointShadowLocalBatchIndices.Clear();
        _pointShadowLocalCasters.Clear();
        Array.Clear(_framePointShadowTiers);
        _pointShadowFramePages = default;
        _pointShadowFrameViews = default;
        _pointShadowFrameCandidates = default;
        _pointShadowFrameBatches = default;
        _pointShadowSampledTiers = 0u;
    }

    private bool TryEnsurePointShadowTextures()
    {
        if (_pointShadowTextures[0] != null)
            return true;

        const string module = "World3DPointShadowSample";
        IReadOnlyList<ShaderTypeLayout> layouts = _system.Shaders.Compiler.LoadModuleFromSource(
            module, module + ".slang", ReadWorldShader(module)).GetTypes();
        GpuStore.ValidateLayout<World3DPointShadowFrameGpu>(layouts.First(t => t.TypeName == nameof(World3DPointShadowFrameGpu)));
        GpuStore.ValidateLayout<World3DPointShadowPageGpu>(layouts.First(t => t.TypeName == nameof(World3DPointShadowPageGpu)));

        try
        {
            for (int i = 0; i < PointShadowTiers.Length; ++i)
            {
                PointShadowTier tier = PointShadowTiers[i];
                _pointShadowTextures[i] = _system.CreateTexture(new TextureDesc
                {
                    Type = TextureType.TwoDArray,
                    Extent = new Extent3D((uint)tier.Resolution, (uint)tier.Resolution, 1u),
                    LayerCount = (uint)(tier.PageCount * PointShadowFaceCount),
                    MipLevels = 1,
                    Format = Format.D32Float,
                    Usage = TextureUsage.DepthStencilAttachment | TextureUsage.Sampled,
                });
            }
            return true;
        }
        catch (InvalidOperationException)
        {
            ReleasePointShadowTextures();
            return false;
        }
        catch (GPUException e) when (e.Error is Error.Unsupported or Error.OutOfMemory)
        {
            ReleasePointShadowTextures();
            return false;
        }
    }

    private void UploadPointShadowUpdates(RenderContext context)
    {
        int candidateCount = _pointShadowCandidates.Count;
        if (candidateCount == 0)
            return;

        if (_pointShadowCullShader == null)
        {
            const string module = "World3DPointShadowCull";
            ShaderSystem shaders = _system.Shaders;
            IReadOnlyList<ShaderTypeLayout> layouts = shaders.Compiler.LoadModuleFromSource(module,
                module + ".slang", ReadWorldShader(module)).GetTypes();
            GpuStore.ValidateLayout<PointShadowCandidateGpu>(layouts.First(t => t.TypeName == nameof(PointShadowCandidateGpu)));
            GpuStore.ValidateLayout<PointShadowBatchGpu>(layouts.First(t => t.TypeName == nameof(PointShadowBatchGpu)));
            Shader shader = shaders.GetShader(module, ShaderKind.Compute);
            ValidateRoot<World3DPointShadowCullRoot>(shader.Reflection.Root
                ?? throw new InvalidOperationException($"{module} has no GPU root."));
            _pointShadowCullShader = shader;
        }

        _pointShadowFrameCandidates = context.UploadTransient<PointShadowCandidateGpu>(CollectionsMarshal.AsSpan(_pointShadowCandidates));
        Span<PointShadowBatchGpu> batches = context.AllocateTransient<PointShadowBatchGpu>(_pointShadowBatches.Count, out _pointShadowFrameBatches);
        for (int i = 0; i < batches.Length; ++i)
            batches[i] = _pointShadowBatches[i].Gpu;
        Span<World3DView> views = context.AllocateTransient<World3DView>(_pointShadowFaceUpdates.Count, out _pointShadowFrameViews);
        for (int i = 0; i < views.Length; ++i)
            views[i] = _pointShadowFaceUpdates[i].View;

        EnsureDrawBuffer(ref _pointShadowArgs, (ulong)candidateCount * IndexedArgsStride);
        EnsureDrawBuffer(ref _pointShadowDrawRefs, (ulong)candidateCount * DrawRefStride);
        EnsureDrawBuffer(ref _pointShadowCounters, (ulong)batches.Length * sizeof(uint));
    }

    private World3DPointShadowFrameGpu GetPointShadowFrameGpu() => _pointShadowFramePages.Buffer == null ? default : new(
        _pointShadowFramePages.View, GetPointShadowSampledTier(0), GetPointShadowSampledTier(1), GetPointShadowSampledTier(2), 0u);

    private GPUTextureId GetPointShadowSampledTier(int tier) => (_pointShadowSampledTiers & (1u << tier)) != 0u
        ? GPUTextureId.FromSampled(_pointShadowTextures[tier]) : default;

    private void ReadPointShadows(RenderGraphBuilder builder, RenderGraphPassBuilder pass, RGUsage usage)
    {
        if (_pointShadowFramePages.Buffer == null)
            return;

        pass.Read(builder.ImportBuffer(_pointShadowFramePages.Buffer, "World3D.PointShadow.Pages"), usage);
        for (int tier = 0; tier < _framePointShadowTiers.Length; ++tier)
        {
            if ((_pointShadowSampledTiers & (1u << tier)) != 0u)
                pass.Read(_framePointShadowTiers[tier], usage);
        }
    }

    private void DisposePointShadowResources()
    {
        ReleasePointShadowTextures();
        _pointShadowArgs?.Dispose(); _pointShadowArgs = null;
        _pointShadowDrawRefs?.Dispose(); _pointShadowDrawRefs = null;
        _pointShadowCounters?.Dispose(); _pointShadowCounters = null;
        _pointShadowCasters.Clear();
        ResetPointShadowRecording();
    }

    private void ReleasePointShadowTextures()
    {
        for (int tier = 0; tier < _pointShadowTextures.Length; ++tier)
        {
            _pointShadowTextures[tier]?.Dispose();
            _pointShadowTextures[tier] = null;
        }
    }

    private sealed class PointShadowPasses(World3DScene scene) : IRenderGraphNode
    {
        private const int PassClear = 0;
        private const int PassCull = 1;
        private const int FirstTierPass = 2;
        private const uint CullGroupSize = 64u;

        private readonly World3DScene _scene = scene;
        private readonly HashSet<GpuBuffer> _payloadPages = [];
        private readonly HashSet<int> _instancePages = [];
        private readonly HashSet<int> _geometryPages = [];
        private readonly HashSet<int> _materialPages = [];

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            uint updatedTiers = 0u;
            foreach (ref readonly PointShadowFaceUpdate update in CollectionsMarshal.AsSpan(_scene._pointShadowFaceUpdates))
                updatedTiers |= 1u << update.Tier;
            uint importedTiers = updatedTiers | _scene._pointShadowSampledTiers;
            for (int tier = 0; tier < PointShadowTiers.Length; ++tier)
            {
                if ((importedTiers & (1u << tier)) != 0u)
                    _scene._framePointShadowTiers[tier] = builder.ImportTexture(_scene._pointShadowTextures[tier]!, $"World3D.PointShadow.Tier{tier}");
            }
            if (updatedTiers == 0u)
                return;

            RGBuffer args = default;
            RGBuffer drawRefs = default;
            RGBuffer views = default;
            if (_scene._pointShadowCandidates.Count != 0)
            {
                _instancePages.Clear();
                _geometryPages.Clear();
                _materialPages.Clear();
                foreach (ref readonly PointShadowCandidateGpu candidate in CollectionsMarshal.AsSpan(_scene._pointShadowCandidates))
                {
                    int slot = (int)candidate.InstanceSlot;
                    World3DInstanceDesc instance = _scene._instances.GetLive(slot).Description;
                    _instancePages.Add(slot / TablePageSize);
                    _geometryPages.Add(instance.Geometry.Slot / TablePageSize);
                    _materialPages.Add(instance.Material.Slot / TablePageSize);
                }

                args = builder.ImportBuffer(_scene._pointShadowArgs!, "World3D.PointShadow.Args");
                drawRefs = builder.ImportBuffer(_scene._pointShadowDrawRefs!, "World3D.PointShadow.DrawRefs");
                RGBuffer counters = builder.ImportBuffer(_scene._pointShadowCounters!, "World3D.PointShadow.Counters");
                views = builder.ImportBuffer(_scene._pointShadowFrameViews.Buffer!, "World3D.PointShadow.Views");
                RGBuffer candidates = builder.ImportBuffer(_scene._pointShadowFrameCandidates.Buffer!, "World3D.PointShadow.Candidates");
                RGBuffer batches = builder.ImportBuffer(_scene._pointShadowFrameBatches.Buffer!, "World3D.PointShadow.Batches");

                RenderGraphPassBuilder clear = builder.AddPass("World3D.PointShadow.Clear", PassClear);
                clear.Write(args, RGUsage.ComputeWrite);
                clear.Write(counters, RGUsage.ComputeWrite);

                RenderGraphPassBuilder cull = builder.AddPass("World3D.PointShadow.Cull", PassCull);
                cull.Read(views, RGUsage.ComputeRead);
                cull.Read(candidates, RGUsage.ComputeRead);
                cull.Read(batches, RGUsage.ComputeRead);
                _scene._gpu.ReadPointCasterTables(builder, cull, RGUsage.ComputeRead, _instancePages, _geometryPages);
                cull.ReadWrite(args, RGUsage.ComputeReadWrite);
                cull.ReadWrite(counters, RGUsage.ComputeReadWrite);
                cull.Write(drawRefs, RGUsage.ComputeWrite);
            }

            for (int tier = 0; tier < PointShadowTiers.Length; ++tier)
            {
                if ((updatedTiers & (1u << tier)) == 0u)
                    continue;

                RenderGraphPassBuilder depth = builder.AddPass($"World3D.PointShadow.Capture{tier}", FirstTierPass + tier);
                // The graph tracks whole arrays. Rendering changes only the attached layers and never samples this array.
                depth.ReadWrite(_scene._framePointShadowTiers[tier], RGUsage.DepthStencil);
                depth.ProducesOutput();
                _payloadPages.Clear();
                ReadOnlySpan<PointShadowBatch> allBatches = CollectionsMarshal.AsSpan(_scene._pointShadowBatches);
                foreach (ref readonly PointShadowFaceUpdate update in CollectionsMarshal.AsSpan(_scene._pointShadowFaceUpdates))
                {
                    if (update.Tier != tier)
                        continue;
                    foreach (ref readonly PointShadowBatch batch in allBatches.Slice(update.FirstBatch, update.BatchCount))
                        _payloadPages.Add(batch.Key.Page);
                }
                if (_payloadPages.Count == 0)
                    continue;

                depth.Read(args, RGUsage.IndirectRead);
                depth.Read(drawRefs, RGUsage.VertexRead);
                depth.Read(views, RGUsage.VertexRead);
                _scene._gpu.ReadPointCasterTables(builder, depth, RGUsage.VertexRead | RGUsage.PixelRead,
                    _instancePages, _geometryPages, _materialPages);
                foreach (GpuBuffer page in _payloadPages)
                    depth.Read(builder.ImportBuffer(page, "World3D.GeometryPayload"), RGUsage.IndexRead | RGUsage.VertexRead);
            }
            _payloadPages.Clear();
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            if (passId < FirstTierPass)
            {
                DispatchCull(context.RenderContext, passId == PassClear);
                return;
            }
            DrawTier(context.RenderContext, passId - FirstTierPass);
        }

        private void DispatchCull(RenderContext context, bool clear)
        {
            uint candidates = (uint)_scene._pointShadowCandidates.Count;
            World3DPointShadowCullRoot root = new(clear ? default : _scene._gpu.Directory!.View,
                clear ? default : _scene._pointShadowFrameViews.View, clear ? default : _scene._pointShadowFrameCandidates.View,
                clear ? default : _scene._pointShadowFrameBatches.View, _scene._pointShadowArgs!.View,
                clear ? default : _scene._pointShadowDrawRefs!.View, _scene._pointShadowCounters!.View,
                candidates, (uint)_scene._pointShadowBatches.Count, clear ? 1u : 0u, 0u);
            using RenderContext.ComputeScope compute = context.BeginCompute();
            compute.Dispatch(_scene._pointShadowCullShader!, in root, new Extent3D((candidates + CullGroupSize - 1) / CullGroupSize, 1u, 1u));
        }

        private void DrawTier(RenderContext context, int tier)
        {
            Texture texture = _scene._pointShadowTextures[tier]!;
            uint packetLimit = _scene._system.ActiveBackend is GraphicsBackend.Vulkan or GraphicsBackend.VulkanCompact
                ? Math.Max(1u, _scene._system.Caps.MaxIndirectDrawCount) : 1u;
            ReadOnlySpan<PointShadowFaceUpdate> updates = CollectionsMarshal.AsSpan(_scene._pointShadowFaceUpdates);
            ReadOnlySpan<PointShadowBatch> batches = CollectionsMarshal.AsSpan(_scene._pointShadowBatches);
            for (int i = 0; i < updates.Length; ++i)
            {
                ref readonly PointShadowFaceUpdate update = ref updates[i];
                if (update.Tier != tier)
                    continue;

                RenderingDesc desc = new()
                {
                    Depth = new DepthAttachment { Target = texture.GetRenderTarget(0u, update.Layer),
                        Load = LoadOp.Clear, Store = StoreOp.Store, Clear = 1f },
                };
                using RenderContext.RenderScope pass = context.BeginRenderPass(desc);
                foreach (ref readonly PointShadowBatch batch in batches.Slice(update.FirstBatch, update.BatchCount))
                {
                    DrawRoute route = batch.Key.Route;
                    for (uint first = 0; first < batch.Gpu.Capacity;)
                    {
                        uint count = Math.Min(packetLimit, batch.Gpu.Capacity - first);
                        uint command = batch.Gpu.FirstCommand + first;
                        BufferSlice args = _scene._pointShadowArgs!.Slice((ulong)command * IndexedArgsStride, (ulong)count * IndexedArgsStride);
                        _scene._gbufferPrograms.DrawShadow(pass, route.SurfaceModule, route.AlphaMode == World3DAlphaMode.Mask,
                            route.Cull, _scene._gpu.Directory!.View, _scene._pointShadowFrameViews.View, _scene._pointShadowDrawRefs!.View,
                            batch.Key.Page, command, args, count, batch.Gpu.ViewIndex);
                        first += count;
                    }
                }
            }
        }
    }

    private readonly record struct PointShadowFaceUpdate(int Tier, uint Layer, int FirstBatch, int BatchCount, World3DView View);
    private readonly record struct PointShadowBatch(DrawKey Key, PointShadowBatchGpu Gpu);

    [StructLayout(LayoutKind.Sequential, Size = 8)]
    private readonly record struct PointShadowCandidateGpu(uint InstanceSlot, uint BatchIndex);

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly record struct PointShadowBatchGpu(uint PageId, uint ViewIndex, uint FirstCommand, uint Capacity);

    [StructLayout(LayoutKind.Sequential, Size = 72)]
    private readonly record struct World3DPointShadowCullRoot(GPUBufferView Directory, GPUBufferView Views, GPUBufferView Candidates,
        GPUBufferView Batches, GPUBufferView Args, GPUBufferView DrawRefs, GPUBufferView Counters,
        uint CandidateCount, uint BatchCount, uint Clear, uint Reserved);

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private readonly record struct World3DPointShadowFrameGpu(
        [field: FieldOffset(0)] GPUBufferView Pages,
        [field: FieldOffset(8)] GPUTextureId Tier0,
        [field: FieldOffset(12)] GPUTextureId Tier1,
        [field: FieldOffset(16)] GPUTextureId Tier2,
        [field: FieldOffset(20)] uint Reserved);

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private readonly record struct World3DPointShadowPageGpu(
        [field: FieldOffset(0)] uint Tier,
        [field: FieldOffset(4)] uint FirstLayer,
        [field: FieldOffset(8)] uint UsableFaces,
        [field: FieldOffset(12)] float Near,
        [field: FieldOffset(16)] float Radius,
        [field: FieldOffset(20)] float Fade0,
        [field: FieldOffset(24)] float Fade1,
        [field: FieldOffset(28)] float Fade2,
        [field: FieldOffset(32)] float Fade3,
        [field: FieldOffset(36)] float Fade4,
        [field: FieldOffset(40)] float Fade5,
        [field: FieldOffset(44)] uint Reserved);
}
