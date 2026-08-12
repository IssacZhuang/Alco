using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// A cascaded shadow map graph node: renders <see cref="CascadeCount"/> cascades into
/// the quadrants of a 2x2 depth atlas, invoking every enabled
/// <see cref="IShadowPassContent"/> in <see cref="Content"/> inside each cascade's
/// pass. Only the first cascade's pass clears the atlas.
/// <br/>The per-frame camera-space cascade view-projection matrices are shared with
/// the caller through the array passed at construction (e.g. filled by
/// <see cref="PBRDeferredPipeline.ComputeShadowCascades"/>); the node folds them into
/// atlas quadrants and uploads the data buffer once, before recording the passes.
/// </summary>
public sealed class ShadowPassNode : AutoDisposable, IRenderGraphNode
{
    /// <summary>The number of shadow cascades (atlas quadrants) the node supports.</summary>
    public const int CascadeCount = 4;

    private readonly RenderGraphTexture _shadowMap;
    private readonly GraphicsValueBuffer<ShadowCascadeData> _cascadeData;
    private readonly Matrix4x4[] _cascadeViewProjections;
    private readonly uint _shadowMapSize;
    private readonly RenderContext _context;

    /// <summary>
    /// Creates the shadow pass node.
    /// </summary>
    /// <param name="rendering">The rendering system, for the pass's render context.</param>
    /// <param name="shadowMap">The depth-only atlas resource (2x2 quadrants of
    /// <paramref name="shadowMapSize"/> texels each).</param>
    /// <param name="cascadeData">The per-cascade data buffer, uploaded once per frame
    /// before the passes are recorded.</param>
    /// <param name="cascadeViewProjections">The shared per-frame camera-space cascade
    /// view-projection matrices (length <see cref="CascadeCount"/>); the caller fills
    /// them before the graph executes. The node folds them into atlas quadrants.</param>
    /// <param name="shadowMapSize">The width of one cascade (atlas quadrant) in texels.</param>
    /// <param name="name">A diagnostic name for the pass's render context.</param>
    public ShadowPassNode(RenderingSystem rendering, RenderGraphTexture shadowMap,
        GraphicsValueBuffer<ShadowCascadeData> cascadeData,
        Matrix4x4[] cascadeViewProjections, uint shadowMapSize, string name = "shadow_pass")
    {
        ArgumentNullException.ThrowIfNull(rendering);
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
        _context = rendering.CreateRenderContext(name);
    }

    /// <summary>The caster content drawn inside each cascade's pass, in list order.
    /// Register content providers here — the pipeline the pass belongs to is not
    /// involved.</summary>
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
        long startTicks = Instrumentation?.BeginCpuTiming() ?? 0;

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

        List<IShadowPassContent> content = Content;
        for (int c = 0; c < CascadeCount; c++)
        {
            // The scissor is essential: geometry outside this cascade's orthographic
            // box can otherwise transform into another atlas quadrant and corrupt
            // that cascade's depth values.
            _context.Begin(_shadowMap.Texture.FrameBuffer, clearDepth: c == 0 ? 1.0f : null);
            _context.SetScissorRect((uint)(c % 2) * _shadowMapSize, (uint)(c / 2) * _shadowMapSize, _shadowMapSize, _shadowMapSize);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].IsEnabled)
                {
                    content[i].OnRenderShadow(_context, c);
                }
            }
            _context.End();
        }

        Instrumentation?.PushCpuTiming(startTicks);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _context.Dispose();
        }
    }
}
