using System.Diagnostics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The volumetric light (god rays) pass of the <see cref="PBRDeferredPipeline"/>:
/// additively blends in-scattered atmospheric radiance into the scene color target.
/// Runs after the lighting node; the lighting data buffer it reads was already
/// uploaded by the lighting node. Disabled (and graph-culled) unless
/// <see cref="PBRDeferredPipeline.VolumetricLightEnabled"/> is set and a volumetric
/// light material exists.
/// </summary>
internal sealed class VolumetricLightNode : IRenderGraphNode
{
    private readonly PBRDeferredPipeline _pipeline;

    internal VolumetricLightNode(PBRDeferredPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public bool IsEnabled => _pipeline.VolumetricLightEnabled && _pipeline.VolumetricLightMaterial != null;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.ReadWrite(_pipeline.SceneColorResource);
        // Headless frames (null destination) still run this pass.
        if (_pipeline.FrameDestinationNull)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        long vlStart = Stopwatch.GetTimestamp();

        // The lighting data buffer was already uploaded by the lighting node, which
        // contains the vlParams the VL shader needs. No re-upload necessary.
        RenderContext vlContext = _pipeline.VolumetricLightPassContext;
        GpuTimestampSampler? gpuTimestamps = _pipeline.GpuTimestamps;
        bool recordGpu = gpuTimestamps != null && gpuTimestamps.ShouldRecord;
        GPUFrameBuffer target = _pipeline.SceneColorResource.Texture.FrameBuffer;
        if (recordGpu)
        {
            vlContext.Begin(target, ReadOnlySpan<ClearColorData>.Empty,
                gpuTimestamps!.QuerySet, PBRDeferredPipeline.VolumetricLightQueryBase, PBRDeferredPipeline.VolumetricLightQueryBase + 1);
        }
        else
        {
            vlContext.Begin(target);
        }
        vlContext.Draw(_pipeline.FullScreenMesh, _pipeline.VolumetricLightMaterial!);
        if (recordGpu)
        {
            vlContext.ResolveTimestampsOnEnd(
                gpuTimestamps!.QuerySet, PBRDeferredPipeline.VolumetricLightQueryBase, 2, gpuTimestamps.ResolveBuffer);
        }
        vlContext.End();

        _pipeline.Profiler.PushValue(_pipeline.VolumetricLightCounter,
            PBRDeferredPipeline.TicksToMilliseconds(Stopwatch.GetTimestamp() - vlStart));
    }
}
