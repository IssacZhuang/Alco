using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A cascaded shadow map graph node: renders <see cref="CascadeCount"/> cascades into
/// the quadrants of a 2x2 depth atlas, invoking every enabled
/// <see cref="IShadowPassContent"/> in <see cref="Content"/> inside each cascade's
/// pass. Only the first cascade's pass clears the atlas.
/// <br/>The per-frame camera-space cascade view-projection matrices are shared with
/// the caller through the array passed at construction (e.g. filled by
/// <see cref="PBRSceneEnvironment.ComputeShadowCascades"/>); the node folds them into
/// atlas quadrants and uploads the data buffer once, before recording the passes.
/// </summary>
public sealed class RGNode_ShadowPass : AutoDisposable, IRenderGraphNode
{
    /// <summary>The number of shadow cascades (atlas quadrants) the node supports.</summary>
    public const int CascadeCount = 4;

    private readonly RenderGraphTexture _shadowMap;
    private readonly GraphicsValueBuffer<ShadowCascadeData> _cascadeData;
    private readonly Matrix4x4[] _cascadeViewProjections;
    private readonly uint _shadowMapSize;

    /// <summary>
    /// Creates the shadow pass node.
    /// </summary>
    /// <param name="shadowMap">The depth-only atlas resource (2x2 quadrants of
    /// <paramref name="shadowMapSize"/> texels each).</param>
    /// <param name="cascadeData">The per-cascade data buffer, uploaded once per frame
    /// before the passes are recorded.</param>
    /// <param name="cascadeViewProjections">The shared per-frame camera-space cascade
    /// view-projection matrices (length <see cref="CascadeCount"/>); the caller fills
    /// them before the graph executes. The node folds them into atlas quadrants.</param>
    /// <param name="shadowMapSize">The width of one cascade (atlas quadrant) in texels.</param>
    public RGNode_ShadowPass(RenderGraphTexture shadowMap,
        GraphicsValueBuffer<ShadowCascadeData> cascadeData,
        Matrix4x4[] cascadeViewProjections, uint shadowMapSize)
    {
        ArgumentNullException.ThrowIfNull(shadowMap);
        ArgumentNullException.ThrowIfNull(cascadeData);
        ArgumentNullException.ThrowIfNull(cascadeViewProjections);
        if (cascadeViewProjections.Length < CascadeCount)
        {
            throw new ArgumentException($"The cascade view-projection array must hold {CascadeCount} entries.", nameof(cascadeViewProjections));
        }
        _shadowMap = shadowMap;
        _cascadeData = cascadeData;
        _cascadeViewProjections = cascadeViewProjections;
        _shadowMapSize = shadowMapSize;
    }

    /// <summary>The caster content drawn inside each cascade's pass, in list order.
    /// Register content providers here — the pipeline the pass belongs to is not
    /// involved.
    /// <br/>Ownership: the pass does not take ownership of registered providers;
    /// disposing them (when disposable) is the caller's responsibility, unlike
    /// nodes handed to <c>RenderGraph.Use</c>, which transfers ownership to the
    /// graph.</summary>
    public List<IShadowPassContent> Content { get; } = new();

    /// <summary>Optional CPU/GPU stage instrumentation.</summary>
    public PassInstrumentation? Instrumentation { get; set; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <summary>The shadow atlas resource the pass renders into.</summary>
    public RenderGraphTexture ShadowMap => _shadowMap;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Write(_shadowMap);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        // Set all four cascade matrices on the CPU side first, then upload once.
        // All four cascade passes see the final complete struct at submit time.
        for (int c = 0; c < CascadeCount; c++)
        {
            float offsetX = (c % 2) - 0.5f;
            float offsetY = 0.5f - (c / 2);
            Matrix4x4 quadrant = Matrix4x4.CreateScale(0.5f, 0.5f, 1.0f) * Matrix4x4.CreateTranslation(offsetX, offsetY, 0.0f);
            Matrix4x4 folded = _cascadeViewProjections[c] * quadrant;
            switch (c)
            {
                case 0: _cascadeData.Value.CascadeViewProjection0 = folded; break;
                case 1: _cascadeData.Value.CascadeViewProjection1 = folded; break;
                case 2: _cascadeData.Value.CascadeViewProjection2 = folded; break;
                default: _cascadeData.Value.CascadeViewProjection3 = folded; break;
            }
        }
        _cascadeData.UpdateBuffer();

        // The GPU timestamp pair brackets all four cascade passes: the begin
        // timestamp rides the first pass, the end timestamp (and the resolve) the
        // last one.
        List<IShadowPassContent> content = Content;
        GPUFrameBuffer shadowFrameBuffer = _shadowMap.Texture.FrameBuffer;
        for (int c = 0; c < CascadeCount; c++)
        {
            float? cascadeClearDepth = c == 0 ? 1.0f : null;
            RenderPassScope pass;
            if (Instrumentation != null && c == 0)
            {
                pass = Instrumentation.BeginSpanPass(context.RenderContext, shadowFrameBuffer,
                    ReadOnlySpan<ClearColorData>.Empty, cascadeClearDepth);
            }
            else if (Instrumentation != null && c == CascadeCount - 1)
            {
                pass = Instrumentation.EndSpanPass(context.RenderContext, shadowFrameBuffer,
                    ReadOnlySpan<ClearColorData>.Empty, cascadeClearDepth);
            }
            else
            {
                pass = context.RenderContext.BeginPass(shadowFrameBuffer, clearDepth: cascadeClearDepth);
            }
            using (pass)
            {
                // The scissor is essential: geometry outside this cascade's orthographic
                // box can otherwise transform into another atlas quadrant and corrupt
                // that cascade's depth values.
                pass.SetScissorRect((uint)(c % 2) * _shadowMapSize, (uint)(c / 2) * _shadowMapSize, _shadowMapSize, _shadowMapSize);
                for (int i = 0; i < content.Count; i++)
                {
                    if (content[i].IsEnabled)
                    {
                        content[i].OnRenderShadow(pass, c);
                    }
                }
                if (c == CascadeCount - 1)
                {
                    Instrumentation?.ScheduleResolve(pass);
                }
            }
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { }
}
