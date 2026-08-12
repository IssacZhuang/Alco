using System.Diagnostics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Optional per-pass instrumentation for the building-block graph nodes: a CPU-side
/// stopwatch pushed to a <see cref="RenderProfiler"/> counter, and GPU-side begin/end
/// timestamps written into a <see cref="GpuTimestampSampler"/> query set slot pair.
/// All fields are optional; a null profiler/sampler disables the corresponding
/// instrumentation. Nodes apply the GPU timestamps around their render pass
/// (see <see cref="BeginPass"/> / <see cref="EndPass"/>) and the CPU timing around
/// their whole <see cref="IRenderGraphNode.Execute"/>.
/// </summary>
public sealed class PassInstrumentation
{
    /// <summary>The profiler receiving the CPU-side stage duration, or null.</summary>
    public RenderProfiler? Profiler { get; set; }

    /// <summary>The profiler counter pushed to, or null to disable CPU timing.</summary>
    public RenderProfileCounterId? CpuCounter { get; set; }

    /// <summary>The GPU timestamp sampler whose query set receives the pass begin/end
    /// timestamps, or null to disable GPU timing.</summary>
    public GpuTimestampSampler? GpuTimestamps { get; set; }

    /// <summary>The begin slot index in the query set; the end timestamp uses
    /// <see cref="GpuQueryBase"/> + 1.</summary>
    public int GpuQueryBase { get; set; }

    /// <summary>Whether GPU timestamps should be recorded this frame.</summary>
    public bool ShouldRecordGpu => GpuTimestamps != null && GpuTimestamps.ShouldRecord;

    /// <summary>
    /// Begins <paramref name="context"/> on <paramref name="target"/>, recording GPU
    /// timestamps when <see cref="ShouldRecordGpu"/> is set.
    /// </summary>
    public void BeginPass(RenderContext context, GPUFrameBuffer target, ReadOnlySpan<ClearColorData> clearColors, float? clearDepth = null)
    {
        if (ShouldRecordGpu)
        {
            context.Begin(target, clearColors, GpuTimestamps!.QuerySet, (uint)GpuQueryBase, (uint)GpuQueryBase + 1, clearDepth);
        }
        else
        {
            context.Begin(target, clearColors, clearDepth);
        }
    }

    /// <summary>
    /// Ends <paramref name="context"/>, resolving the GPU timestamps recorded since
    /// <see cref="BeginPass"/> when <see cref="ShouldRecordGpu"/> is set.
    /// </summary>
    public void EndPass(RenderContext context)
    {
        if (ShouldRecordGpu)
        {
            context.ResolveTimestampsOnEnd(GpuTimestamps!.QuerySet, (uint)GpuQueryBase, 2, GpuTimestamps.ResolveBuffer);
        }
        context.End();
    }

    /// <summary>Returns the current timestamp for a later <see cref="PushCpuTiming"/>,
    /// or 0 when CPU timing is disabled.</summary>
    public long BeginCpuTiming()
    {
        return Profiler != null && CpuCounter.HasValue ? Stopwatch.GetTimestamp() : 0;
    }

    /// <summary>Pushes the elapsed milliseconds since <paramref name="startTicks"/> to
    /// the profiler counter. No-op when CPU timing is disabled.</summary>
    public void PushCpuTiming(long startTicks)
    {
        if (Profiler != null && CpuCounter.HasValue)
        {
            Profiler.PushValue(CpuCounter.Value, (double)(Stopwatch.GetTimestamp() - startTicks) / Stopwatch.Frequency * 1000.0);
        }
    }
}
