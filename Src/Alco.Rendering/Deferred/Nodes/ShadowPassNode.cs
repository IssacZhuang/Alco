using System.Diagnostics;
using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// The shadow map pass of the <see cref="PBRDeferredPipeline"/>: renders the
/// <see cref="PBRDeferredPipeline.ShadowCascadeCount"/> PSSM cascades into the
/// quadrants of the 2x2 shadow atlas, invoking every enabled
/// <see cref="IShadowRenderNode"/> inside each cascade's pass. Only the first
/// cascade's pass clears the atlas. Disabled (and graph-culled) when
/// <see cref="PBRDeferredPipeline.ShadowEnabled"/> is false: the lighting node then
/// declares no shadow map read, so this node's write is unused and it records no
/// GPU work at all.
/// <br/>The node's CPU-side stopwatch accumulation is self-contained: the measured
/// total is pushed to the pipeline's shadow profiler counter after the last cascade.
/// </summary>
internal sealed class ShadowPassNode : IRenderGraphNode
{
    private readonly PBRDeferredPipeline _pipeline;
    private long _elapsedTicks;
    private long _stageStartTicks;

    internal ShadowPassNode(PBRDeferredPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public bool IsEnabled => _pipeline.ShadowEnabled;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Write(_pipeline.ShadowMapResource);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        _elapsedTicks = 0;
        List<IRenderNode> passNodes = _pipeline.PassNodes;
        RenderContext shadowContext = _pipeline.ShadowPassContext;

        // Set all four cascade matrices on the CPU side first, then upload once.
        // All four cascade passes see the final complete struct at submit time.
        for (int c = 0; c < PBRDeferredPipeline.ShadowCascadeCount; c++)
        {
            float offsetX = (c % 2) - 0.5f;
            float offsetY = 0.5f - (c / 2);
            Matrix4x4 quadrant = Matrix4x4.CreateScale(0.5f, 0.5f, 1.0f) * Matrix4x4.CreateTranslation(offsetX, offsetY, 0.0f);
            _pipeline.SetCascadeViewProjection(c, _pipeline.CascadeViewProjections[c] * quadrant);
        }
        _pipeline.FlushShadowDataBuffer();

        for (int c = 0; c < PBRDeferredPipeline.ShadowCascadeCount; c++)
        {
            BeginCascade(c);
            for (int i = 0; i < passNodes.Count; i++)
            {
                if (passNodes[i].IsEnabled && passNodes[i] is IShadowRenderNode shadowNode)
                {
                    shadowNode.OnRenderShadow(shadowContext, c);
                }
            }
            shadowContext.End();
            _elapsedTicks += Stopwatch.GetTimestamp() - _stageStartTicks;
        }
        _pipeline.Profiler.PushValue(_pipeline.ShadowCounter, PBRDeferredPipeline.TicksToMilliseconds(_elapsedTicks));
    }

    /// <summary>
    /// Begins one cascade's pass: opens the pass (clearing the atlas only for
    /// cascade 0) and applies the cascade's scissor rect.
    /// </summary>
    private void BeginCascade(int cascadeIndex)
    {
        _stageStartTicks = Stopwatch.GetTimestamp();

        // The scissor is essential: geometry outside this cascade's orthographic
        // box can otherwise transform into another atlas quadrant and corrupt that
        // cascade's depth values.
        RenderContext shadowContext = _pipeline.ShadowPassContext;
        shadowContext.Begin(_pipeline.ShadowMapResource.Texture.FrameBuffer, clearDepth: cascadeIndex == 0 ? 1.0f : null);
        uint shadowMapSize = _pipeline.ShadowMapSize;
        shadowContext.SetScissorRect(
            (uint)(cascadeIndex % 2) * shadowMapSize,
            (uint)(cascadeIndex / 2) * shadowMapSize,
            shadowMapSize,
            shadowMapSize);
    }
}
