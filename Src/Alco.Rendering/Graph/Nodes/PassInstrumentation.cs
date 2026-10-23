using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Optional GPU timestamp instrumentation for graph nodes that brackets a pass with a <see cref="GpuTimestampSampler"/> query set slot pair.
/// A null sampler disables instrumentation; CPU-side node timing is handled by the graph itself.
/// </summary>
public sealed class PassInstrumentation
{
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
    /// <param name="context">The render context that records the pass.</param>
    /// <param name="target">The framebuffer to render to.</param>
    /// <param name="clearColors">Optional color attachment clear values.</param>
    /// <param name="clearDepth">Optional depth clear value.</param>
    /// <param name="clearStencil">Optional stencil clear value.</param>
    /// <returns>The pass scope; dispose it (or use <c>using</c>) to close the pass.</returns>
    public RenderPassScope BeginPass(
        RenderContext context,
        GPUFrameBuffer target,
        ReadOnlySpan<ClearColorData> clearColors,
        float? clearDepth = null,
        uint? clearStencil = null)
    {
        if (ShouldRecordGpu)
        {
            return context.BeginPass(
                target,
                clearColors,
                GpuTimestamps!.QuerySet,
                (uint)GpuQueryBase,
                (uint)GpuQueryBase + 1,
                clearDepth,
                clearStencil);
        }

        return context.BeginPass(target, clearColors, clearDepth, clearStencil);
    }

    /// <summary>
    /// Begins the first pass of a measured span, writing only the begin timestamp.
    /// Open the last pass of the span with <see cref="EndSpanPass"/> and resolve there.
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
}
