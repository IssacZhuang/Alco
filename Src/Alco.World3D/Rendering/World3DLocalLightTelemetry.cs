using Alco.Graphics;
using GpuBuffer = Alco.Graphics.Buffer;

namespace Alco.World3D;

/// <summary>Contains deferred readback of coarse and fine light-bin overflow.</summary>
/// <param name="FrameId">The completed render-context frame, or zero before the first readback.</param>
/// <param name="CoarseOverflowClusters">Coarse bins that exceeded their capacity.</param>
/// <param name="CoarseDroppedLights">Candidate references dropped from coarse bins.</param>
/// <param name="FineOverflowClusters">Fine clusters that exceeded their capacity.</param>
/// <param name="FineDroppedLights">Candidate references dropped from fine clusters.</param>
public readonly record struct World3DLocalLightClusterStats(ulong FrameId, uint CoarseOverflowClusters,
    uint CoarseDroppedLights, uint FineOverflowClusters, uint FineDroppedLights);

public sealed partial class World3DScene
{
    private const ulong LocalLightCountersBytes = 4 * sizeof(uint);
    private readonly LocalLightReadbackSlot[] _localLightReadbacks = new LocalLightReadbackSlot[3];
    private int _recordedLocalLightReadback = -1;
    private BufferSlice _recordedLocalLightCounters;

    /// <summary>Enables per-pass GPU timestamp collection when the backend supports it.</summary>
    public bool GpuProfilingEnabled
    {
        get => _graph.ProfilingEnabled;
        set
        {
            _graph.ProfilingEnabled = value;
            if (value)
                _graph.MaxTimestampPasses = 128;
        }
    }

    /// <summary>Gets the render-context frame ID of the latest timestamp result, or zero when unavailable.</summary>
    public ulong GpuPassReportFrame => _graph.CompletedGpuFrameId;

    /// <summary>Gets the timestamped pass names and durations for <see cref="GpuPassReportFrame"/>.</summary>
    public ReadOnlySpan<RenderGraphPassReport> GpuPassReports => _graph.CompletedGpuPasses;

    /// <summary>Gets the profiled CPU time in milliseconds for direct light selection, shadow scheduling, and cluster preparation.</summary>
    public double LastRecordedLocalLightCpuMilliseconds { get; private set; }

    /// <summary>Gets the latest completed overflow sample without waiting for the GPU.</summary>
    public World3DLocalLightClusterStats LastCompletedLocalLightClusters { get; private set; }

    private void PrepareLocalLightTelemetry(RenderContext context)
    {
        _recordedLocalLightReadback = -1;
        _recordedLocalLightCounters = default;
        if (!LocalLights.EnableOverflowTelemetry || _system.ActiveBackend == GraphicsBackend.NoGPU)
            return;

        int writeSlot = -1;
        Span<uint> counters = stackalloc uint[4];
        for (int i = 0; i < _localLightReadbacks.Length; ++i)
        {
            ref LocalLightReadbackSlot slot = ref _localLightReadbacks[i];
            if (slot.FrameId != 0 && context.IsFrameCompleted(slot.FrameId))
            {
                if (slot.FrameId > LastCompletedLocalLightClusters.FrameId)
                {
                    slot.Buffer!.GetData<uint>(counters);
                    LastCompletedLocalLightClusters = new(slot.FrameId, counters[0], counters[1], counters[2], counters[3]);
                }
                slot.FrameId = 0;
            }
            if (writeSlot < 0 && slot.FrameId == 0)
                writeSlot = i;
        }

        BufferSlice source = GetLocalLightClusterCountersGpuSlice();
        if (writeSlot < 0 || source.IsEmpty)
            return;
        _localLightReadbacks[writeSlot].Buffer ??= _system.CreateBuffer(LocalLightCountersBytes, MemoryType.Readback);
        _recordedLocalLightReadback = writeSlot;
        _recordedLocalLightCounters = source;
    }

    private void ReconcileLocalLightTelemetry(bool submitted)
    {
        if (submitted && _recordedLocalLightReadback >= 0)
            _localLightReadbacks[_recordedLocalLightReadback].FrameId = _recordedFrame;
    }

    private void DisposeLocalLightTelemetry()
    {
        foreach (LocalLightReadbackSlot slot in _localLightReadbacks)
            slot.Buffer?.Dispose();
        Array.Clear(_localLightReadbacks);
    }

    private struct LocalLightReadbackSlot
    {
        public GpuBuffer? Buffer;
        public ulong FrameId;
    }

    private sealed class LocalLightTelemetryPass(World3DScene scene) : IRenderGraphNode
    {
        void IRenderGraphNode.Setup(RenderGraphBuilder builder)
        {
            if (scene._recordedLocalLightReadback < 0)
                return;
            GpuBuffer readback = scene._localLightReadbacks[scene._recordedLocalLightReadback].Buffer!;
            RenderGraphPassBuilder pass = builder.AddPass("World3D.LocalLights.OverflowReadback");
            pass.Read(builder.ImportBuffer(scene._recordedLocalLightCounters.Buffer!, "World3D.LocalLights.Counters"), RGUsage.TransferRead);
            pass.Write(builder.ImportBuffer(readback, "World3D.LocalLights.Readback", RGUsage.HostRead), RGUsage.TransferWrite);
            pass.ProducesOutput();
        }

        void IRenderGraphNode.Execute(in RenderGraphContext context, int passId)
        {
            GpuBuffer readback = scene._localLightReadbacks[scene._recordedLocalLightReadback].Buffer!;
            context.RenderContext.CopyBuffer(scene._recordedLocalLightCounters, readback);
        }
    }
}
