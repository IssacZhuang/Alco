using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const uint ForwardArgsStride = 16;
    private const uint ForwardDrawRefStride = 8;
    private const uint ForwardSortKeyStride = 8;
    private const uint ForwardCounterBytes = 16;

    private ForwardPrograms? _forwardPrograms;
    private Shader? _forwardCullKeyShader;
    private Shader? _forwardSortShader;
    private Shader? _forwardEmitArgsShader;
    private Shader? _forwardSnapshotShader;
    private bool _forwardComputeLoaded;

    private GpuBuffer? _forwardSortKeys;
    private GpuBuffer? _forwardCounters;
    private GpuBuffer? _forwardArgs;
    private GpuBuffer? _forwardDrawRefs;

    private ulong _forwardSubmittedFrame;
    private uint _cachedForwardVisibleDraws;
    private bool _recordedForwardActive;
    private uint _recordedForwardCandidates;
    private uint _recordedForwardPaddedCount;
    private Vector4 _recordedForwardSunDirIntensity;
    private Vector4 _recordedForwardSunColorAmbient;
    private Vector4 _recordedForwardSkyHorizonRefr;
    private Vector4 _recordedForwardSkyZenithGi;

    /// <summary>Gets or sets runtime settings for GPU-driven transparent and glass forward rendering.</summary>
    public World3DForwardSettings Forward { get; set; } = new();

    /// <summary>Gets the recorded GPU-driven forward transparency telemetry for the latest frame.</summary>
    public World3DForwardStats LastRecordedForward { get; private set; } =
        new(false, 0u, 0u, 0, false);

    internal BufferSlice GetForwardSortKeysGpuSlice() => _recordedForwardPaddedCount == 0
        ? default
        : _forwardSortKeys!.Slice(0, (ulong)_recordedForwardPaddedCount * ForwardSortKeyStride);

    internal BufferSlice GetForwardDrawRefsGpuSlice() => _recordedForwardCandidates == 0
        ? default
        : _forwardDrawRefs!.Slice(0, (ulong)_recordedForwardCandidates * ForwardDrawRefStride);

    private void PrepareForwardPasses(RenderContext context)
    {
        _recordedForwardActive = false;
        _recordedForwardCandidates = 0u;
        _recordedForwardPaddedCount = 0u;

        if (_forwardSubmittedFrame != 0 && _forwardSubmittedFrame != _recordedFrame &&
            context.IsFrameCompleted(_forwardSubmittedFrame) && _forwardCounters != null)
        {
            Span<uint> counters = stackalloc uint[4];
            _system.ReadBuffer(_forwardCounters, MemoryMarshal.AsBytes(counters));
            _cachedForwardVisibleDraws = counters[0];
            _forwardSubmittedFrame = 0;
        }

        World3DForwardSettings settings = Forward;
        if (!_recordedWorld || !settings.Enabled)
        {
            LastRecordedForward = new World3DForwardStats(false, 0u, _cachedForwardVisibleDraws, 0, false);
            return;
        }

        int maxInst = Math.Clamp(settings.MaxTransparentInstances, 0, MaxTableSlots);
        _forwardLayout.Stage(context, _system, _gpu, _instances, _stagedDrawLods, maxInst);
        uint candidates = (uint)_forwardLayout.CandidateCount;
        if (candidates == 0u || _forwardLayout.Buckets.Count == 0)
        {
            LastRecordedForward = new World3DForwardStats(false, 0u, 0u, 0, false);
            return;
        }

        EnsureForwardShaders();
        uint padded = BitOperations.RoundUpToPowerOf2(Math.Max(256u, candidates));
        EnsureDrawBuffer(ref _forwardSortKeys, (ulong)padded * ForwardSortKeyStride);
        EnsureDrawBuffer(ref _forwardCounters, ForwardCounterBytes);
        EnsureDrawBuffer(ref _forwardArgs, (ulong)candidates * ForwardArgsStride);
        EnsureDrawBuffer(ref _forwardDrawRefs, (ulong)candidates * ForwardDrawRefStride);

        EvaluateFilteredSkyAndSun(Lighting, out Vector3 sunDir, out Vector3 sunColor, out float sunIntensity, out Vector3 horizon, out Vector3 zenith);

        _recordedForwardActive = true;
        _recordedForwardCandidates = candidates;
        _recordedForwardPaddedCount = padded;
        _recordedForwardSunDirIntensity = new Vector4(sunDir, sunIntensity);
        _recordedForwardSunColorAmbient = new Vector4(sunColor, Lighting.AmbientFloor);
        _recordedForwardSkyHorizonRefr = new Vector4(horizon, settings.EnableRefraction ? Math.Max(settings.RefractionScale, 0f) : 0f);
        _recordedForwardSkyZenithGi = new Vector4(zenith, MathF.Max(0f, VoxelGi.IndirectDiffuseIntensity));

        LastRecordedForward = new World3DForwardStats(
            true,
            candidates,
            _cachedForwardVisibleDraws,
            _forwardLayout.Buckets.Count,
            settings.EnableRefraction);
    }

    private void ReconcileForwardPasses(bool submitted)
    {
        if (_recordedWorld)
            _forwardLayout.Reconcile(submitted);
        if (submitted && _recordedWorld && _recordedForwardActive)
            _forwardSubmittedFrame = _recordedFrame;
        else if (!submitted)
            _forwardSubmittedFrame = 0;
    }

    private void DisposeForwardPasses()
    {
        _forwardPrograms?.Dispose();
        _forwardPrograms = null;
        _forwardLayout.Dispose();
        _forwardSortKeys?.Dispose(); _forwardSortKeys = null;
        _forwardCounters?.Dispose(); _forwardCounters = null;
        _forwardArgs?.Dispose(); _forwardArgs = null;
        _forwardDrawRefs?.Dispose(); _forwardDrawRefs = null;
    }

    private void EnsureForwardShaders()
    {
        _forwardPrograms ??= new ForwardPrograms(_system);
        if (_forwardComputeLoaded)
            return;

        _forwardCullKeyShader = LoadSceneCompute<ForwardCullKeyRoot>("World3DForwardCullKey");
        _forwardSortShader = LoadSceneCompute<ForwardSortRoot>("World3DForwardSort");
        _forwardEmitArgsShader = LoadSceneCompute<ForwardEmitArgsRoot>("World3DForwardEmitArgs");
        _forwardSnapshotShader = LoadSceneCompute<ForwardSnapshotRoot>("World3DForwardSnapshot");
        _forwardComputeLoaded = true;
    }

    private sealed class ForwardPrograms : IDisposable
    {
        private const string StandardSurfaceModule = "World3DPbrStandard";
        private const string ForwardTemplate = "World3DForward";

        private readonly ShaderSystem _shaders;
        private readonly Dictionary<string, Material> _materials = [];

        public ForwardPrograms(RenderingSystem system)
        {
            _shaders = system.Shaders;
            ShaderCompiler compiler = _shaders.Compiler;
            compiler.RegisterVirtualModule(ForwardTemplate, ForwardTemplate + ".slang", ReadWorldShader(ForwardTemplate));
            GetMaterial(StandardSurfaceModule);
        }

        public Material GetMaterial(string surfaceModule)
        {
            if (_materials.TryGetValue(surfaceModule, out Material? material))
                return material;

            Shader shader = _shaders.Compose(ForwardTemplate, surfaceModule, ShaderKind.Graphics);
            material = shader.CreateMaterial($"{shader.Name}:Forward");
            ValidateRoot<World3DForwardRoot>(material.Reflection.Root
                ?? throw new InvalidOperationException($"{material.Name} did not reflect a root layout."));
            material.Blend = BlendState.PremultipliedAlpha;
            material.Rasterization = RasterizationState.CullNone;
            material.DepthStencilState = DepthStencilState.ReadReverseZ;
            _materials.Add(surfaceModule, material);
            return material;
        }

        public void Dispose() => _materials.Clear();
    }

    private sealed class ForwardPasses(World3DScene scene, RenderChain chain) : IRenderGraphNode
    {
        private const int PassClearCounters = 0;
        private const int PassCullKey = 1;
        private const int PassSort = 2;
        private const int PassEmitArgs = 3;
        private const int PassSnapshot = 4;
        private const int PassDraw = 5;

        private readonly World3DScene _scene = scene;
        private readonly RenderChain _chain = chain;
        private RGTexture _hdrInRG;
        private RGTexture _hdrOutRG;
        private RGBuffer _candidatesRG;
        private RGBuffer _sortKeysRG;
        private RGBuffer _countersRG;
        private RGBuffer _argsRG;
        private RGBuffer _drawRefsRG;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _hdrInRG = default;
            _hdrOutRG = default;
            _candidatesRG = default;
            _sortKeysRG = default;
            _countersRG = default;
            _argsRG = default;
            _drawRefsRG = default;

            if (!_scene._recordedWorld || !_scene._recordedForwardActive || !_chain.Current.IsValid
                || !_scene._gbufferTargets.Depth.IsValid
                || _scene.LightingDebugView != World3DLightingDebugView.None)
                return;

            _hdrInRG = _chain.Current;
            _candidatesRG = builder.ImportBuffer(_scene._forwardLayout.Buffer!, "World3D.Forward.Candidates");
            _sortKeysRG = builder.ImportBuffer(_scene._forwardSortKeys!, "World3D.Forward.SortKeys");
            _countersRG = builder.ImportBuffer(_scene._forwardCounters!, "World3D.Forward.Counters");
            _argsRG = builder.ImportBuffer(_scene._forwardArgs!, "World3D.Forward.Args");
            _drawRefsRG = builder.ImportBuffer(_scene._forwardDrawRefs!, "World3D.Forward.DrawRefs");

            RGBuffer views = builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views");
            RGBuffer shadowFrame = builder.ImportBuffer(_scene._frameShadowData.Buffer!, "World3D.ShadowFrame");

            RenderGraphPassBuilder clearPass = builder.AddPass("World3D.Forward.Clear", PassClearCounters);
            clearPass.Write(_countersRG, RGUsage.ComputeWrite);

            RenderGraphPassBuilder cullPass = builder.AddPass("World3D.Forward.CullKey", PassCullKey);
            _scene._gpu.ReadTables(builder, cullPass, RGUsage.ComputeRead, materials: false);
            cullPass.Read(views, RGUsage.ComputeRead);
            cullPass.Read(shadowFrame, RGUsage.ComputeRead);
            cullPass.Read(_candidatesRG, RGUsage.ComputeRead);
            if (_scene._frameCurrHiZ.IsValid)
                cullPass.Read(_scene._frameCurrHiZ, RGUsage.ComputeRead);
            cullPass.Write(_sortKeysRG, RGUsage.ComputeWrite);
            cullPass.ReadWrite(_countersRG, RGUsage.ComputeReadWrite);

            RenderGraphPassBuilder sortPass = builder.AddPass("World3D.Forward.Sort", PassSort);
            sortPass.ReadWrite(_sortKeysRG, RGUsage.ComputeReadWrite);

            RenderGraphPassBuilder emitPass = builder.AddPass("World3D.Forward.EmitArgs", PassEmitArgs);
            _scene._gpu.ReadTables(builder, emitPass, RGUsage.ComputeRead, materials: false);
            emitPass.Read(_sortKeysRG, RGUsage.ComputeRead);
            emitPass.Write(_argsRG, RGUsage.ComputeWrite);
            emitPass.Write(_drawRefsRG, RGUsage.ComputeWrite);

            _hdrOutRG = builder.CreateTexture(new RGTextureDesc
            {
                Name = "World3D.Forward.HDR",
                Extent = builder.GetExtent(_hdrInRG),
                Format = Format.Rgba16Float,
            });
            RenderGraphPassBuilder copyPass = builder.AddPass("World3D.Forward.Snapshot", PassSnapshot);
            copyPass.Read(_hdrInRG, RGUsage.ComputeRead);
            copyPass.Write(_hdrOutRG, RGUsage.ComputeWrite);

            RenderGraphPassBuilder drawPass = builder.AddPass("World3D.Forward.Draw", PassDraw);
            drawPass.ReadWrite(_hdrOutRG, RGUsage.ColorAttachment);
            drawPass.Read(_scene._gbufferTargets.Depth, RGUsage.DepthStencilRead);
            drawPass.Read(views, RGUsage.VertexRead | RGUsage.PixelRead);
            drawPass.Read(shadowFrame, RGUsage.PixelRead);
            drawPass.Read(_hdrInRG, RGUsage.PixelRead);
            drawPass.Read(_argsRG, RGUsage.IndirectRead);
            drawPass.Read(_drawRefsRG, RGUsage.VertexRead);
            _scene._gpu.ReadTables(builder, drawPass, RGUsage.VertexRead | RGUsage.PixelRead, materials: true);
            foreach (GpuBuffer page in _scene._forwardLayout.PayloadPages)
                drawPass.Read(builder.ImportBuffer(page, "World3D.ForwardPayload"), RGUsage.VertexRead);
            if (_scene._frameShadowArrayRG.IsValid) drawPass.Read(_scene._frameShadowArrayRG, RGUsage.PixelRead);
            if (_scene._frameShadowMinMaxRG.IsValid) drawPass.Read(_scene._frameShadowMinMaxRG, RGUsage.PixelRead);
            if (_scene._frameFarHeightRG.IsValid) drawPass.Read(_scene._frameFarHeightRG, RGUsage.PixelRead);
            if (_scene._frameCloudShadowRG.IsValid) drawPass.Read(_scene._frameCloudShadowRG, RGUsage.PixelRead);
            if (_scene._recordedVoxelGiEnabled && _scene._frameGiOutputDiffuseRG.IsValid)
                drawPass.Read(_scene._frameGiOutputDiffuseRG, RGUsage.PixelRead);

            _chain.Advance(_hdrOutRG);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            uint padded = _scene._recordedForwardPaddedCount;
            uint candidates = _scene._recordedForwardCandidates;

            if (passId == PassClearCounters)
            {
                using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
                ForwardSortRoot clearRoot = new(default, _scene._forwardCounters!.View, padded, 3u, 0u, 0u);
                compute.Dispatch(_scene._forwardSortShader!, in clearRoot, new Extent3D(1u, 1u, 1u));
                return;
            }

            if (passId == PassCullKey)
            {
                using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
                uint flags = 0u;
                if (_scene.Forward.UseHiZOcclusion) flags |= 1u;
                if (_scene.Forward.SortFrontToBack) flags |= 2u;
                ForwardCullKeyRoot cullRoot = new(
                    _scene._gpu.Directory!.View,
                    _scene._frameViews.View,
                    _scene._frameShadowData.View,
                    _scene._forwardLayout.Buffer!.View,
                    _scene._forwardSortKeys!.View,
                    _scene._forwardCounters!.View,
                    candidates,
                    padded,
                    flags,
                    _scene.MaximumDrawDistance);
                compute.Dispatch(_scene._forwardCullKeyShader!, in cullRoot, new Extent3D((padded + 63u) / 64u, 1u, 1u));
                return;
            }

            if (passId == PassSort)
            {
                using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
                GPUBufferView sortView = _scene._forwardSortKeys!.View;
                uint localTiles = padded / 256u;
                uint globalGroups = (padded + 255u) / 256u;

                ForwardSortRoot localInit = new(sortView, default, padded, 0u, 0u, 0u);
                compute.Dispatch(_scene._forwardSortShader!, in localInit, new Extent3D(localTiles, 1u, 1u));

                for (uint k = 512u; k <= padded; k <<= 1)
                {
                    for (uint j = k >> 1; j >= 256u; j >>= 1)
                    {
                        compute.Barrier(Stage.Compute, Access.ShaderRead | Access.ShaderWrite,
                            Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                        ForwardSortRoot stepRoot = new(sortView, default, padded, 1u, k, j);
                        compute.Dispatch(_scene._forwardSortShader!, in stepRoot, new Extent3D(globalGroups, 1u, 1u));
                    }

                    compute.Barrier(Stage.Compute, Access.ShaderRead | Access.ShaderWrite,
                        Stage.Compute, Access.ShaderRead | Access.ShaderWrite);
                    ForwardSortRoot mergeRoot = new(sortView, default, padded, 2u, k, 0u);
                    compute.Dispatch(_scene._forwardSortShader!, in mergeRoot, new Extent3D(localTiles, 1u, 1u));
                }
                return;
            }

            if (passId == PassEmitArgs)
            {
                using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
                ForwardEmitArgsRoot emitRoot = new(
                    _scene._gpu.Directory!.View,
                    _scene._forwardSortKeys!.View,
                    _scene._forwardArgs!.View,
                    _scene._forwardDrawRefs!.View,
                    candidates,
                    0u);
                compute.Dispatch(_scene._forwardEmitArgsShader!, in emitRoot, new Extent3D((candidates + 63u) / 64u, 1u, 1u));
                return;
            }

            if (passId == PassSnapshot)
            {
                using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();
                Extent3D full = context.GetExtent(_hdrOutRG);
                ForwardSnapshotRoot snapRoot = new(
                    GPUTextureId.FromSampled(context.GetTexture(_hdrInRG)),
                    GPUTextureId.FromStorage(context.GetTexture(_hdrOutRG)),
                    full.X,
                    full.Y,
                    0u,
                    0u);
                compute.Dispatch(_scene._forwardSnapshotShader!, in snapRoot, new Extent3D((full.X + 7u) / 8u, (full.Y + 7u) / 8u, 1u));
                return;
            }

            RenderingDesc attachments = new()
            {
                Colors = [new ColorAttachment { Target = context.GetRenderTarget(_hdrOutRG), Load = LoadOp.Load, Store = StoreOp.Store }],
                Depth = new DepthAttachment { Target = context.GetRenderTarget(_scene._gbufferTargets.Depth), Load = LoadOp.Load, Store = StoreOp.Store },
            };
            using RenderContext.RenderScope pass = context.RenderContext.BeginRenderPass(attachments);

            int packetLimit = _scene._system.ActiveBackend is GraphicsBackend.Vulkan or GraphicsBackend.VulkanCompact
                ? (int)Math.Clamp(_scene._system.Caps.MaxIndirectDrawCount, 1u, (uint)int.MaxValue)
                : 1;

            bool giValid = _scene._recordedVoxelGiEnabled && _scene._frameGiOutputDiffuseRG.IsValid;
            uint drawFlags = 0u;
            if (_scene.Forward.EnableRefraction) drawFlags |= 1u;
            if (giValid) drawFlags |= 2u;

            GPUTextureId depthId = GPUTextureId.FromSampled(context.GetTexture(_scene._gbufferTargets.Depth));
            GPUTextureId snapshotId = GPUTextureId.FromSampled(context.GetTexture(_hdrInRG));
            GPUTextureId giDiffId = giValid ? GPUTextureId.FromSampled(context.GetTexture(_scene._frameGiOutputDiffuseRG)) : GPUTextureId.Invalid;

            foreach (ForwardBucket bucket in _scene._forwardLayout.Buckets)
            {
                Material material = _scene._forwardPrograms!.GetMaterial(bucket.SurfaceModule);
                for (int first = 0; first < bucket.CandidateCount;)
                {
                    int count = Math.Min(packetLimit, bucket.CandidateCount - first);
                    uint start = (uint)(bucket.FirstCandidate + first);
                    BufferSlice argsSlice = _scene._forwardArgs!.Slice((ulong)start * ForwardArgsStride, (ulong)count * ForwardArgsStride);
                    World3DForwardRoot root = new(
                        _scene._recordedForwardSunDirIntensity,
                        _scene._recordedForwardSunColorAmbient,
                        _scene._recordedForwardSkyHorizonRefr,
                        _scene._recordedForwardSkyZenithGi,
                        _scene._gpu.Directory!.View,
                        _scene._frameViews.View,
                        _scene._forwardDrawRefs!.View,
                        _scene._frameShadowData.View,
                        _scene._framePointLights.IsEmpty ? default : _scene._framePointLights.View,
                        depthId,
                        snapshotId,
                        giDiffId,
                        start,
                        (uint)_scene._recordedPointLightCount,
                        drawFlags);
                    pass.DrawIndirect(material, in root, DepthStencilState.ReadReverseZ, argsSlice, (uint)count, stride: ForwardArgsStride);
                    first += count;
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private readonly record struct ForwardCullKeyRoot(
        GPUBufferView Directory,
        GPUBufferView Views,
        GPUBufferView ShadowFrame,
        GPUBufferView Candidates,
        GPUBufferView SortKeys,
        GPUBufferView Counters,
        uint CandidateCount,
        uint PaddedCount,
        uint Flags,
        float MaximumDistance);

    [StructLayout(LayoutKind.Sequential, Size = 32)]
    private readonly record struct ForwardSortRoot(
        GPUBufferView SortKeys,
        GPUBufferView Counters,
        uint PaddedCount,
        uint Mode,
        uint StageK,
        uint SubStepJ);

    [StructLayout(LayoutKind.Sequential, Size = 40)]
    private readonly record struct ForwardEmitArgsRoot(
        GPUBufferView Directory,
        GPUBufferView SortKeys,
        GPUBufferView IndirectArgs,
        GPUBufferView DrawRefs,
        uint CandidateCount,
        uint Reserved0);

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private readonly record struct ForwardSnapshotRoot(
        GPUTextureId SrcTex,
        GPUTextureId DstTex,
        uint Width,
        uint Height,
        uint Reserved0,
        uint Reserved1);

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private readonly record struct World3DForwardRoot(
        [field: FieldOffset(0)] Vector4 SunDirectionAndIntensity,
        [field: FieldOffset(16)] Vector4 SunColorAndAmbient,
        [field: FieldOffset(32)] Vector4 SkyHorizonAndRefrScale,
        [field: FieldOffset(48)] Vector4 SkyZenithAndGiScale,
        [field: FieldOffset(64)] GPUBufferView Directory,
        [field: FieldOffset(72)] GPUBufferView Views,
        [field: FieldOffset(80)] GPUBufferView DrawRefs,
        [field: FieldOffset(88)] GPUBufferView ShadowFrame,
        [field: FieldOffset(96)] GPUBufferView PointLights,
        [field: FieldOffset(104)] GPUTextureId DepthTex,
        [field: FieldOffset(108)] GPUTextureId ColorSnapshotTex,
        [field: FieldOffset(112)] GPUTextureId GiDiffuseTex,
        [field: FieldOffset(116)] uint FirstDraw,
        [field: FieldOffset(120)] uint PointLightCount,
        [field: FieldOffset(124)] uint Flags);
}
