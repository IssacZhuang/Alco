using System.Numerics;
using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

public sealed partial class World3DScene
{
    private RenderContext? _context;
    private ulong _recordedFrame;
    private ulong _recordedDiscardVersion;
    private World3DCamera _recordedCamera;
    private World3DView _recordedView;
    private World3DView? _submittedView;
    private bool _recordingSucceeded;

    /// <summary>Records incremental scene updates and renders to the output when supplied.</summary>
    /// <param name="context">The same active render context for every frame, recording on queue zero.</param>
    /// <param name="camera">The primary view's double-position camera.</param>
    /// <param name="viewport">The headless viewport size; a valid output supplies its own size.</param>
    /// <param name="view">Receives the frame's camera-relative view.</param>
    /// <param name="output">An optional color output target.</param>
    /// <param name="outputFinalUsage">The output's next usage outside the graph.</param>
    /// <param name="renderWorld">Whether to render instead of only synchronizing and clearing the output.</param>
    /// <param name="deltaTime">Elapsed frame time in seconds for temporal GI convergence.</param>
    /// <returns>Whether the camera and viewport were valid and the frame was recorded.</returns>
    /// <remarks>Call after BeginFrame, then submit or discard before editing the scene. Discard the frame if recording throws.</remarks>
    public bool TryRecordFrame(RenderContext context, in World3DCamera camera, Extent2D viewport, out World3DView view,
        RenderGraphTarget output = default, RGUsage outputFinalUsage = RGUsage.None, bool renderWorld = true,
        float deltaTime = 1f / 60f)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(context.IsDisposed, context);
        if (context.CurrentFrameId == 0 || (_context != null && !ReferenceEquals(_context, context)))
            throw new InvalidOperationException("World3D requires a single active render context for its submission stream.");
        BeginEdit();
        if (output.IsValid)
            viewport = output.Extent;
        if (!World3DView.TryCreate(camera, viewport, _submittedView?.OriginAnchorCell, out view))
            return false;
        if (!_graph.Viewport.Equals(viewport))
            _graph.Resize(viewport);

        _context = context;
        _recordedFrame = context.CurrentFrameId;
        _recordedDiscardVersion = context.DiscardVersion;
        _recordedCamera = camera;
        _recordedView = view;
        _recordedWorld = output.IsValid && renderWorld;
        _gpu.ResetJobs();
        LastRecordedSync = default;
        LastRecordedDraws = default;
        LastRecordedOcclusion = default;
        LastRecordedShadows = default;
        ulong geometryBytes = PrepareGeometryCopies(context);
        PrepareOcclusion(context, viewport);
        PrepareGeometryPasses(context);
        PrepareVoxelGi(context, in camera, viewport, in view, deltaTime);
        _gpu.PrepareTables(context);
        _graph.Execute(context, output, outputFinalUsage: outputFinalUsage);
        _recordingSucceeded = true;
        LastRecordedSync = new World3DSceneSyncStats(geometryBytes, _gpu.GeometryPatches, _gpu.MaterialPatches,
            _gpu.InstancePatches, _gpu.InitializedTablePages);
        return true;
    }

    private void ReconcileFrame()
    {
        if (_recordedFrame == 0)
            return;
        RenderContext context = _context!;
        if (context.CurrentFrameId == _recordedFrame && !context.IsDisposed)
            return;

        bool submitted = _recordingSucceeded && !context.IsDisposed && context.DiscardVersion == _recordedDiscardVersion;
        _submittedWorld = submitted && _recordedWorld;
        CompleteGeometryCopies(submitted);
        ReconcileOcclusion(submitted);
        ReconcileShadows(submitted);
        ReconcileFarShadow(submitted);
        ReconcileVoxelGi(submitted);
        _stagedDrawLods.Clear();
        if (_recordedWorld)
        {
            if (submitted)
            {
                _drawLayout.Commit();
                _dirtyDrawInstances.Clear();
            }
            else
            {
                RefreshDrawLayout();
            }
        }
        if (submitted)
        {
            _submittedView = _recordedView;
            _gpu.CommitTables();
            if (_retireOnSubmit.Count != 0)
            {
                if (context.IsFrameCompleted(_recordedFrame))
                {
                    foreach (Action reclaim in _retireOnSubmit)
                        reclaim();
                }
                else
                {
                    Action[] retirements = _retireOnSubmit.ToArray();
                    _system.Defer(() => _retired.Enqueue(retirements));
                }
                _retireOnSubmit.Clear();
            }
        }
        else
        {
            // A discarded write may have replaced the previous writer in the graph's hazard cache.
            if (!context.IsDisposed)
                context.Wait();
            _graph.MarkCompleted();
        }
        _gpu.ResetJobs();
        _recordingSucceeded = false;
        _recordedFrame = 0;
    }

    internal RenderGraphStatistics LastGraphStatistics => _graph.Statistics;

    internal BufferSlice GetInstanceGpuSlice(int slot) => _gpu.GetInstanceSlice(slot);

    internal BufferSlice GetGeometryLodGpuSlice(int slot, int lod) => _gpu.GetGeometrySlice(slot, lod);

    internal BufferSlice GetGeometryPayloadGpuSlice(WorldGeometryHandle handle, int lod)
    {
        ReconcileFrame();
        if (!TryGetGeometry(handle, out GeometryEntry? geometry) || geometry.Resident[lod] is not ResidentLod resident)
            return default;
        GeometryLodGpu data = resident.Gpu;
        ulong byteCount = data.IndexByteOffset - data.VertexByteOffset + (ulong)data.IndexCount * sizeof(uint);
        return resident.Allocation.Buffer.Slice(resident.Allocation.Offset, byteCount);
    }

    private sealed class SyncNode(GpuStore gpu) : IRenderGraphNode
    {
        private const int Transfer = 0;
        private const int Scatter = 1;
        private readonly GpuStore _gpu = gpu;
        private readonly Dictionary<GpuBuffer, RGBuffer> _imports = new();
        private readonly HashSet<GpuBuffer> _reads = [];
        private readonly HashSet<GpuBuffer> _writes = [];

        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            _imports.Clear();
            _reads.Clear();
            _writes.Clear();
            if (_gpu.Copies.Count != 0)
            {
                RenderGraphPassBuilder pass = builder.AddPass("World3D.SceneSync.Copy", Transfer);
                pass.ProducesOutput();
                foreach (CopyJob copy in _gpu.Copies)
                {
                    DeclareRead(builder, pass, copy.Source.Buffer!, RGUsage.TransferRead);
                    DeclareWrite(builder, pass, copy.Destination.Buffer!, RGUsage.TransferRead | RGUsage.TransferWrite);
                }
                _reads.Clear();
                _writes.Clear();
            }
            if (_gpu.Scatters.Count != 0)
            {
                RenderGraphPassBuilder pass = builder.AddPass("World3D.SceneSync.Scatter", Scatter);
                pass.ProducesOutput();
                foreach (ScatterJob job in _gpu.Scatters)
                {
                    DeclareRead(builder, pass, job.Patches.Buffer!, RGUsage.ComputeRead);
                    DeclareRead(builder, pass, job.Slots.Buffer!, RGUsage.ComputeRead);
                    DeclareWrite(builder, pass, job.Destination, RGUsage.ComputeReadWrite);
                }
                _reads.Clear();
                _writes.Clear();
            }
            _imports.Clear();
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            RenderContext commands = context.RenderContext;
            if (passId == Transfer)
            {
                foreach (CopyJob job in _gpu.Copies)
                    commands.CopyBuffer(job.Source, job.Destination);
                return;
            }
            using RenderContext.ComputeScope compute = commands.BeginCompute();
            foreach (ScatterJob job in _gpu.Scatters)
            {
                ScatterRoot root = job.Root;
                compute.Dispatch(_gpu.ScatterShader, in root, new Extent3D((root.PatchCount + 63) / 64, 1, 1));
            }
        }

        private void DeclareRead(RenderGraphBuilder builder, RenderGraphPassBuilder pass, GpuBuffer buffer, RGUsage usage)
        {
            if (_reads.Add(buffer))
                pass.Read(Import(builder, buffer), usage);
        }

        private void DeclareWrite(RenderGraphBuilder builder, RenderGraphPassBuilder pass, GpuBuffer buffer, RGUsage usage)
        {
            if (_writes.Add(buffer))
                pass.ReadWrite(Import(builder, buffer), usage);
        }

        private RGBuffer Import(RenderGraphBuilder builder, GpuBuffer buffer)
        {
            if (!_imports.TryGetValue(buffer, out RGBuffer handle))
            {
                handle = builder.ImportBuffer(buffer, "World3D.SceneSync");
                _imports.Add(buffer, handle);
            }
            return handle;
        }
    }

    private sealed class ClearNode(World3DScene scene) : IRenderGraphNode
    {
        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            if (builder.HasOutput && !scene._recordedWorld)
                builder.AddPass("World3D.Clear").Write(builder.Output, RGUsage.ColorAttachmentWrite);
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            RenderingDesc clear = new()
            {
                Colors = [new ColorAttachment { Target = context.Output.Target, Clear = Vector4.UnitW, Load = LoadOp.Clear, Store = StoreOp.Store }],
            };
            using var render = context.RenderContext.BeginRenderPass(clear);
        }
    }
}
