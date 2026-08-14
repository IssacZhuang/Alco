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
    /// Begins a pass on <paramref name="context"/> rendering to <paramref name="target"/>,
    /// recording GPU timestamps when <see cref="ShouldRecordGpu"/> is set.
    /// </summary>
    /// <returns>The pass scope; dispose it (or use <c>using</c>) to close the pass.</returns>
    public RenderPassScope BeginPass(RenderContext context, GPUFrameBuffer target, ReadOnlySpan<ClearColorData> clearColors, float? clearDepth = null)
    {
        if (ShouldRecordGpu)
        {
            return context.BeginPass(target, clearColors, GpuTimestamps!.QuerySet, (uint)GpuQueryBase, (uint)GpuQueryBase + 1, clearDepth);
        }

        return context.BeginPass(target, clearColors, clearDepth);
    }

    /// <summary>
    /// Begins the first pass of a measured span: only the begin timestamp is
    /// written, so consecutive passes can be bracketed by one timestamp pair —
    /// open the last pass of the span with <see cref="EndSpanPass"/> and resolve
    /// there via <see cref="ScheduleResolve"/>. No-op pass opening when GPU
    /// timing is disabled.
    /// </summary>
    /// <returns>The pass scope; dispose it (or use <c>using</c>) to close the pass.</returns>
    public RenderPassScope BeginSpanPass(RenderContext context, GPUFrameBuffer target, ReadOnlySpan<ClearColorData> clearColors, float? clearDepth = null)
    {
        if (ShouldRecordGpu)
        {
            return context.BeginPass(target, clearColors, GpuTimestamps!.QuerySet, (uint)GpuQueryBase, null, clearDepth);
        }

        return context.BeginPass(target, clearColors, clearDepth);
    }

    /// <summary>
    /// Begins the last pass of a span opened with <see cref="BeginSpanPass"/>:
    /// only the end timestamp is written. Call <see cref="ScheduleResolve"/> on
    /// the returned scope so the span's slot pair resolves when the pass closes.
    /// </summary>
    /// <returns>The pass scope; dispose it (or use <c>using</c>) to close the pass.</returns>
    public RenderPassScope EndSpanPass(RenderContext context, GPUFrameBuffer target, ReadOnlySpan<ClearColorData> clearColors, float? clearDepth = null)
    {
        if (ShouldRecordGpu)
        {
            return context.BeginPass(target, clearColors, GpuTimestamps!.QuerySet, null, (uint)GpuQueryBase + 1, clearDepth);
        }

        return context.BeginPass(target, clearColors, clearDepth);
    }

    /// <summary>
    /// Schedules the resolve of the GPU timestamps recorded since <see cref="BeginPass"/>
    /// into the sampler's resolve buffer, to run when <paramref name="pass"/> closes.
    /// No-op when <see cref="ShouldRecordGpu"/> is not set.
    /// </summary>
    /// <summary>
    /// Schedules the resolve of the GPU timestamps recorded since <see cref="BeginPass"/>
    /// into the sampler's resolve buffer, to run when <paramref name="pass"/> closes.
    /// No-op when <see cref="ShouldRecordGpu"/> is not set.
    /// </summary>
    public void ScheduleResolve(RenderPassScope pass)
    {
        if (ShouldRecordGpu)
        {
            GpuTimestampSampler sampler = GpuTimestamps!;
            if (sampler.UsesPaddedPairs)
            {
                // Shared sampler: this stage's pair resolves into its own
                // stride-aligned region (the pair index follows the query base).
                sampler.ResolvePair(pass, GpuQueryBase / 2);
            }
            else
            {
                // Private sampler: the whole (fully written) range at offset 0.
                sampler.ResolveAll(pass);
            }
        }
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
