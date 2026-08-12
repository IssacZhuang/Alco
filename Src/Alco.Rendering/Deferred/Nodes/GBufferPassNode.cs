using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The G-buffer pass of the <see cref="PBRDeferredPipeline"/>: clears the four
/// G-buffer attachments plus depth, then invokes every enabled
/// <see cref="IGBufferRenderNode"/> inside the single open pass. Always enabled —
/// every frame needs a G-buffer. Owns the stage's GPU timestamp slots
/// (GBufferQueryBase) and pushes both the readback-driven GPU timings and the
/// CPU-side stage duration to the pipeline profiler.
/// </summary>
internal sealed class GBufferPassNode : IRenderGraphNode
{
    private readonly PBRDeferredPipeline _pipeline;

    internal GBufferPassNode(PBRDeferredPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Write(_pipeline.GBufferResource);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        long stageStartTicks = Stopwatch.GetTimestamp();

        // Read back GPU timestamps from the previous sample (guaranteed GPU-complete
        // via the sampler's throttled interval) and push them to the profiler.
        _pipeline.ReadbackPipelineTimestamps();

        ReadOnlySpan<ClearColorData> clearColors = stackalloc ClearColorData[4]
        {
            new(0, Vector4.Zero),
            new(1, new Vector4(0.5f, 0.5f, 1.0f, 1.0f)),
            new(2, Vector4.Zero),
            new(3, Vector4.Zero),
        };
        RenderContext gbufferContext = _pipeline.GBufferPassContext;
        GpuTimestampSampler? gpuTimestamps = _pipeline.GpuTimestamps;
        bool recordGpu = gpuTimestamps != null && gpuTimestamps.ShouldRecord;
        if (recordGpu)
        {
            gbufferContext.Begin(_pipeline.GBufferResource.Texture.FrameBuffer, clearColors,
                gpuTimestamps!.QuerySet, PBRDeferredPipeline.GBufferQueryBase, PBRDeferredPipeline.GBufferQueryBase + 1, 1.0f);
        }
        else
        {
            gbufferContext.Begin(_pipeline.GBufferResource.Texture.FrameBuffer, clearColors, 1.0f);
        }

        List<IRenderNode> passNodes = _pipeline.PassNodes;
        for (int i = 0; i < passNodes.Count; i++)
        {
            if (passNodes[i].IsEnabled && passNodes[i] is IGBufferRenderNode gbufferNode)
            {
                gbufferNode.OnRenderGBuffer(gbufferContext, _pipeline.GBufferLayout);
            }
        }

        if (recordGpu)
        {
            gbufferContext.ResolveTimestampsOnEnd(
                gpuTimestamps!.QuerySet, PBRDeferredPipeline.GBufferQueryBase, 2, gpuTimestamps.ResolveBuffer);
        }
        gbufferContext.End();
        _pipeline.Profiler.PushValue(_pipeline.GBufferCounter,
            PBRDeferredPipeline.TicksToMilliseconds(Stopwatch.GetTimestamp() - stageStartTicks));
    }
}
