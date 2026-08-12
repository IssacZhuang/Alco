using System.Diagnostics;
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The deferred lighting pass of the <see cref="PBRDeferredPipeline"/>: assembles and
/// uploads the per-frame lighting constants, then draws the full-screen lighting
/// material into the scene color target (which shares the G-buffer's depth, so the
/// depth filled by the G-buffer pass stays available to the forward pass — no depth
/// copy). Always enabled.
/// <br/>Setup reads the G-buffer, conditionally reads the shadow map (only while
/// <see cref="PBRDeferredPipeline.ShadowEnabled"/> is set — this is what graph-culls
/// the shadow pass when shadows are off) and the imported AO/GI plugin outputs when
/// present, writes the scene color, and roots the graph on headless frames (null
/// destination) so the lit scene is still produced.
/// </summary>
internal sealed class LightingNode : IRenderGraphNode
{
    private readonly PBRDeferredPipeline _pipeline;

    internal LightingNode(PBRDeferredPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Read(_pipeline.GBufferResource);
        if (_pipeline.ShadowEnabled)
        {
            builder.Read(_pipeline.ShadowMapResource);
        }
        if (_pipeline.AoImport != null)
        {
            builder.Read(_pipeline.AoImport);
        }
        if (_pipeline.GiDiffuseImport != null)
        {
            builder.Read(_pipeline.GiDiffuseImport);
            builder.Read(_pipeline.GiSpecularImport!);
        }
        builder.Write(_pipeline.SceneColorResource);
        if (_pipeline.FrameDestinationNull)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        CameraPerspectiveBuffer? camera = _pipeline.Camera;
        if (camera == null)
        {
            throw new InvalidOperationException("RenderLighting requires a camera (call SetCamera first).");
        }

        long lightingStart = Stopwatch.GetTimestamp();

        Matrix4x4.Invert(camera.Data.ViewProjectionMatrix, out Matrix4x4 invViewProjection);
        _pipeline.AssembleLightingData(invViewProjection);
        _pipeline.LightingDataBufferTyped.UpdateBuffer(_pipeline.CurrentLightingData);

        RenderContext lightingContext = _pipeline.LightingPassContext;
        GpuTimestampSampler? gpuTimestamps = _pipeline.GpuTimestamps;
        bool recordGpu = gpuTimestamps != null && gpuTimestamps.ShouldRecord;
        GPUFrameBuffer target = _pipeline.SceneColorResource.Texture.FrameBuffer;
        if (recordGpu)
        {
            lightingContext.Begin(target, ReadOnlySpan<ClearColorData>.Empty,
                gpuTimestamps!.QuerySet, PBRDeferredPipeline.LightingQueryBase, PBRDeferredPipeline.LightingQueryBase + 1);
        }
        else
        {
            lightingContext.Begin(target);
        }
        lightingContext.Draw(_pipeline.FullScreenMesh, _pipeline.LightingMaterial);
        if (recordGpu)
        {
            lightingContext.ResolveTimestampsOnEnd(
                gpuTimestamps!.QuerySet, PBRDeferredPipeline.LightingQueryBase, 2, gpuTimestamps.ResolveBuffer);
        }
        lightingContext.End();

        _pipeline.Profiler.PushValue(_pipeline.LightingCounter,
            PBRDeferredPipeline.TicksToMilliseconds(Stopwatch.GetTimestamp() - lightingStart));
    }
}
