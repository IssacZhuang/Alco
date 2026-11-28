using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private const uint LightClusterGroupSize = 64;
    private const uint LightClusterFinePerAxis = 4;
    private const uint LightClusterDepthSlices = 24;
    private const uint LightClusterCoarseDepthSlices = LightClusterDepthSlices / LightClusterFinePerAxis;
    private const uint LightClusterWideTile = 8;
    private const uint LightClusterChildren = 64;
    private const uint LightClusterCoarseLimit = 128;
    private const uint LightClusterMaxCoarseCount = 6_144;
    private const int LightClusterMaxCandidates = 4_096;

    private Shader? _localLightClusterClearShader;
    private Shader? _localLightClusterBinShader;
    private Shader? _localLightClusterRefineShader;
    private GpuBuffer? _localLightClusterCounters;
    private World3DLightClusterGridGpu _recordedLightClusterGrid;
    private Vector2 _recordedLightClusterInverseProjection;
    private RGBuffer _frameClusterLightsRG;
    private RGBuffer _frameClusterCoarseLightsRG;
    private RGBuffer _frameClusterFineMasksRG;

    private void PrepareLocalLightClusters()
    {
        _recordedLightClusterGrid = default;
        if (!_recordedWorld || _recordedLocalLightCount == 0)
            return;

        EnsureLocalLightClusterShaders();
        _localLightClusterCounters ??= _system.CreateBuffer((ulong)Unsafe.SizeOf<World3DLightClusterCountersGpu>(), MemoryType.GpuOnly);

        Extent2D viewport = _graph.Viewport;
        uint tileSize = 16;
        uint coarseX;
        uint coarseY;
        while (true)
        {
            uint coarseTileSize = tileSize * LightClusterFinePerAxis;
            coarseX = LightClusterDivideRoundUp(viewport.X, coarseTileSize);
            coarseY = LightClusterDivideRoundUp(viewport.Y, coarseTileSize);
            if ((ulong)coarseX * coarseY * LightClusterCoarseDepthSlices <= LightClusterMaxCoarseCount)
                break;
            tileSize *= 2;
        }

        float logNear = MathF.Log2(_recordedView.EyeLocalNear.W);
        float logSpan = MathF.Max(MathF.Log2(_recordedLocalLightDepth) - logNear, 1f / 1024f);
        _recordedLightClusterGrid = new(tileSize, coarseX, coarseY, logNear,
            LightClusterDepthSlices / logSpan, _recordedLocalLightDepth);

        // Each of the first two inverse-VP rows is a unit camera axis times its inverse projection scale.
        Matrix4x4 inverse = _recordedView.CameraInvVP;
        _recordedLightClusterInverseProjection = new(
            AxisLength(inverse.M11, inverse.M12, inverse.M13),
            AxisLength(inverse.M21, inverse.M22, inverse.M23));

        static float AxisLength(double x, double y, double z) => (float)Math.Sqrt(x * x + y * y + z * z);
    }

    private void EnsureLocalLightClusterShaders()
    {
        if (_localLightClusterRefineShader != null)
            return;

        IReadOnlyList<ShaderTypeLayout> types = _system.Shaders.Compiler.LoadModule("World3DLightClusterCommon").GetTypes();
        GpuStore.ValidateLayout<World3DLightClusterGridGpu>(types.First(t => t.TypeName == nameof(World3DLightClusterGridGpu)));
        GpuStore.ValidateLayout<World3DLightClusterFrameGpu>(types.First(t => t.TypeName == nameof(World3DLightClusterFrameGpu)));
        GpuStore.ValidateLayout<World3DLocalLightingFrameGpu>(types.First(t => t.TypeName == nameof(World3DLocalLightingFrameGpu)));
        GpuStore.ValidateLayout<World3DLightClusterCandidateGpu>(types.First(t => t.TypeName == nameof(World3DLightClusterCandidateGpu)));
        GpuStore.ValidateLayout<World3DClusterLightGpu>(types.First(t => t.TypeName == nameof(World3DClusterLightGpu)));
        GpuStore.ValidateLayout<World3DLightClusterCountersGpu>(types.First(t => t.TypeName == nameof(World3DLightClusterCountersGpu)));
        _localLightClusterClearShader = LoadSceneCompute<LightClusterClearRoot>("World3DLightClusterClear");
        _localLightClusterBinShader = LoadSceneCompute<LightClusterBinRoot>("World3DLightClusterBin");
        _localLightClusterRefineShader = LoadSceneCompute<LightClusterRefineRoot>("World3DLightClusterRefine");
    }

    private void DisposeLocalLightClusters()
    {
        _localLightClusterCounters?.Dispose();
        _localLightClusterCounters = null;
    }

    // An optional graph pass can consume these counters with a transfer read; lighting never reads them back.
    private BufferSlice GetLocalLightClusterCountersGpuSlice() => _recordedLightClusterGrid.TileSize == 0
        ? default
        : _localLightClusterCounters!.Slice(0, (ulong)Unsafe.SizeOf<World3DLightClusterCountersGpu>());

    private void ReadClusteredLocalLighting(RenderGraphBuilder builder, RenderGraphPassBuilder pass, RGUsage usage)
    {
        if (!_frameClusterFineMasksRG.IsValid)
            return;

        pass.Read(_frameClusterLightsRG, usage);
        pass.Read(_frameClusterCoarseLightsRG, usage);
        pass.Read(_frameClusterFineMasksRG, usage);
        ReadPointShadows(builder, pass, usage);
    }

    private GPUBufferView GetLocalLightingFrameView(in RenderGraphContext context) =>
        _frameClusterCoarseLightsRG.IsValid ? context.GetBuffer(_frameClusterCoarseLightsRG).View : default;

    private static uint LightClusterDivideRoundUp(uint count, uint step) => 1u + (count - 1u) / step;

    private static uint GetLightClusterBinCount(World3DLightClusterGridGpu grid) =>
        (grid.CoarseTilesX + LightClusterDivideRoundUp(grid.CoarseTilesX, LightClusterWideTile)) *
        (grid.CoarseTilesY + LightClusterDivideRoundUp(grid.CoarseTilesY, LightClusterWideTile)) * LightClusterCoarseDepthSlices;

    private sealed class LocalLightClusterPasses(World3DScene scene) : IRenderGraphNode
    {
        private const int Clear = 0;
        private const int Bin = 1;
        private const int Refine = 2;
        private const uint VectorBytes = 16;

        private readonly World3DScene _scene = scene;
        private RGBuffer _bins;
        private RGBuffer _spheres;

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _scene._frameClusterLightsRG = default;
            _scene._frameClusterCoarseLightsRG = default;
            _scene._frameClusterFineMasksRG = default;
            if (!builder.HasOutput || _scene._recordedLightClusterGrid.TileSize == 0)
                return;

            World3DLightClusterGridGpu grid = _scene._recordedLightClusterGrid;
            uint candidates = (uint)_scene._recordedLocalLightCount;
            uint wordsPerBin = LightClusterDivideRoundUp(candidates, 32u);
            uint coarseCount = grid.CoarseTilesX * grid.CoarseTilesY * LightClusterCoarseDepthSlices;
            _bins = builder.CreateBuffer(new RGBufferDesc
            {
                Name = "World3D.LocalLightClusters.Bins",
                SizeBytes = (ulong)GetLightClusterBinCount(grid) * wordsPerBin * sizeof(uint),
            });
            _spheres = builder.CreateBuffer(new RGBufferDesc
            {
                Name = "World3D.LocalLightClusters.ViewSpheres",
                SizeBytes = (ulong)candidates * VectorBytes,
            });
            _scene._frameClusterLightsRG = builder.CreateBuffer(new RGBufferDesc
            {
                Name = "World3D.LocalLightClusters.Lights",
                SizeBytes = (ulong)candidates * (uint)Unsafe.SizeOf<World3DClusterLightGpu>(),
            });
            _scene._frameClusterCoarseLightsRG = builder.CreateBuffer(new RGBufferDesc
            {
                Name = "World3D.LocalLightClusters.CoarseLights",
                SizeBytes = (ulong)Unsafe.SizeOf<World3DLocalLightingFrameGpu>() + (ulong)coarseCount * LightClusterCoarseLimit * sizeof(uint),
            });
            _scene._frameClusterFineMasksRG = builder.CreateBuffer(new RGBufferDesc
            {
                Name = "World3D.LocalLightClusters.FineMasks",
                SizeBytes = (ulong)coarseCount * LightClusterChildren * VectorBytes,
            });

            RGBuffer counters = builder.ImportBuffer(_scene._localLightClusterCounters!, "World3D.LocalLightClusters.Counters");
            RenderGraphPassBuilder clear = builder.AddPass("World3D.LocalLightClusters.Clear", Clear);
            clear.Write(_bins, RGUsage.ComputeWrite);
            clear.Write(counters, RGUsage.ComputeWrite);

            RenderGraphPassBuilder bin = builder.AddPass("World3D.LocalLightClusters.Bin", Bin);
            _scene._gpu.ReadLocalLights(builder, bin, RGUsage.ComputeRead);
            bin.Read(builder.ImportBuffer(_scene._frameLocalLightCandidates.Buffer!, "World3D.LocalLightCandidates"), RGUsage.ComputeRead);
            bin.Read(builder.ImportBuffer(_scene._frameViews.Buffer!, "World3D.Views"), RGUsage.ComputeRead);
            bin.ReadWrite(_bins, RGUsage.ComputeReadWrite);
            bin.Write(_spheres, RGUsage.ComputeWrite);
            bin.Write(_scene._frameClusterLightsRG, RGUsage.ComputeWrite);

            RenderGraphPassBuilder refine = builder.AddPass("World3D.LocalLightClusters.Refine", Refine);
            refine.Read(_bins, RGUsage.ComputeRead);
            refine.Read(_spheres, RGUsage.ComputeRead);
            refine.Write(_scene._frameClusterCoarseLightsRG, RGUsage.ComputeWrite);
            refine.Write(_scene._frameClusterFineMasksRG, RGUsage.ComputeWrite);
            refine.ReadWrite(counters, RGUsage.ComputeReadWrite);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            World3DLightClusterGridGpu grid = _scene._recordedLightClusterGrid;
            uint candidates = (uint)_scene._recordedLocalLightCount;
            uint wordsPerBin = LightClusterDivideRoundUp(candidates, 32u);
            GPUBufferView bins = context.GetBuffer(_bins).View;
            using RenderContext.ComputeScope compute = context.RenderContext.BeginCompute();

            if (passId == Clear)
            {
                uint binCount = GetLightClusterBinCount(grid);
                LightClusterClearRoot root = new(bins, _scene._localLightClusterCounters!.View, binCount, wordsPerBin);
                compute.Dispatch(_scene._localLightClusterClearShader!, in root,
                    new Extent3D(LightClusterDivideRoundUp(binCount * wordsPerBin, LightClusterGroupSize), 1, 1));
                return;
            }

            GPUBufferView spheres = context.GetBuffer(_spheres).View;
            if (passId == Bin)
            {
                LightClusterBinRoot root = new(_scene._gpu.Directory!.View, _scene._frameViews.View,
                    _scene._frameLocalLightCandidates.View, context.GetBuffer(_scene._frameClusterLightsRG).View,
                    spheres, bins, grid, _scene._recordedLightClusterInverseProjection, candidates, wordsPerBin);
                compute.Dispatch(_scene._localLightClusterBinShader!, in root,
                    new Extent3D(LightClusterDivideRoundUp(candidates, LightClusterGroupSize), 1, 1));
                return;
            }

            GpuBuffer coarseBuffer = context.GetBuffer(_scene._frameClusterCoarseLightsRG);
            // Refine writes the coarse list and its header. The lighting passes, not Refine, read the referenced lights and shadows.
            World3DLightClusterFrameGpu clusters = new(context.GetBuffer(_scene._frameClusterLightsRG).View,
                coarseBuffer.Slice((ulong)Unsafe.SizeOf<World3DLocalLightingFrameGpu>()).View,
                context.GetBuffer(_scene._frameClusterFineMasksRG).View, grid);
            World3DLocalLightingFrameGpu lighting = new(clusters, _scene.GetPointShadowFrameGpu());
            LightClusterRefineRoot refine = new(bins, spheres, _scene._localLightClusterCounters!.View, coarseBuffer.View,
                lighting, _scene._recordedLightClusterInverseProjection, new Vector2(context.Viewport.X, context.Viewport.Y), wordsPerBin, 0u);
            compute.Dispatch(_scene._localLightClusterRefineShader!, in refine,
                new Extent3D(grid.CoarseTilesX, grid.CoarseTilesY, LightClusterCoarseDepthSlices));
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private readonly record struct World3DLightClusterGridGpu(
        uint TileSize, uint CoarseTilesX, uint CoarseTilesY, float LogNear, float SlicesPerLog2Unit, float FarDepth);

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private readonly record struct World3DLightClusterFrameGpu(
        GPUBufferView Lights, GPUBufferView CoarseLights, GPUBufferView FineMasks, World3DLightClusterGridGpu Grid);

    [StructLayout(LayoutKind.Sequential, Size = 72)]
    private readonly record struct World3DLocalLightingFrameGpu(
        World3DLightClusterFrameGpu Clusters, World3DPointShadowFrameGpu PointShadows);

    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private readonly record struct World3DClusterLightGpu(
        Vector4 EyeRelativePositionAndRadius, Vector4 ColorAndIntensity, float SourceRadius, uint ShadowIndexPlusOne,
        uint Reserved0, uint Reserved1);

    [StructLayout(LayoutKind.Sequential, Size = 16)]
    private readonly record struct World3DLightClusterCountersGpu(
        uint CoarseOverflowClusters, uint CoarseDroppedLights, uint FineOverflowClusters, uint FineDroppedLights);

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private readonly record struct LightClusterClearRoot(GPUBufferView Bins, GPUBufferView Counters, uint BinCount, uint WordsPerBin);

    [StructLayout(LayoutKind.Sequential, Size = 88)]
    private readonly record struct LightClusterBinRoot(
        GPUBufferView Directory, GPUBufferView Views, GPUBufferView Candidates, GPUBufferView Lights,
        GPUBufferView Spheres, GPUBufferView Bins, World3DLightClusterGridGpu Grid,
        Vector2 InverseProjectionScale, uint CandidateCount, uint WordsPerBin);

    [StructLayout(LayoutKind.Sequential, Size = 128)]
    private readonly record struct LightClusterRefineRoot(
        GPUBufferView Bins, GPUBufferView Spheres, GPUBufferView Counters, GPUBufferView FrameData,
        World3DLocalLightingFrameGpu Lighting, Vector2 InverseProjectionScale, Vector2 ViewportSize, uint WordsPerBin, uint Reserved0);
}
