using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A deferred lighting graph node: draws a full-screen lighting material into the
/// scene color target, resolving the G-buffer (plus the shadow map and AO/GI plugin
/// outputs when wired) into lit HDR scene color. The scene color target shares the
/// G-buffer's depth attachment (see
/// <see cref="RenderGraphTextureDescriptor.DepthSource"/>), so the depth filled by
/// the G-buffer pass stays available to later forward content — no depth copy.
/// <br/>Per-frame data assembly and uniform uploads are delegated to
/// <see cref="PrepareData"/>, invoked at the start of <see cref="Execute"/> — before
/// any pass recording, since buffer writes are queue-side operations that must
/// precede the recording of the pass that reads them. Plugin outputs
/// are wired without the node knowing their types: set <see cref="AoInput"/> /
/// <see cref="GiDiffuseInput"/> / <see cref="GiSpecularInput"/> (and bind the matching
/// material parameter), or use <see cref="ExtraReads"/> for arbitrary additional
/// inputs so their producers survive culling.
/// </summary>
public sealed class RGNode_DeferredLighting : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraph _graph;
    private readonly Mesh _fullScreenMesh;

    /// <summary>
    /// Creates the lighting node.
    /// </summary>
    /// <param name="rendering">The rendering system, for the full-screen mesh.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="material">The lighting material, with the G-buffer (and any static)
    /// bindings already set. Not owned by the node.</param>
    /// <param name="gbuffer">The G-buffer resource read by the lighting pass.</param>
    /// <param name="sceneColor">The scene color resource written by the lighting pass.</param>
    public RGNode_DeferredLighting(RenderingSystem rendering, RenderGraph graph,
        GraphicsMaterial material, RenderGraphTexture gbuffer, RenderGraphTexture sceneColor)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(gbuffer);
        ArgumentNullException.ThrowIfNull(sceneColor);
        _graph = graph;
        Material = material;
        GBuffer = gbuffer;
        SceneColor = sceneColor;
        _fullScreenMesh = rendering.MeshFullScreen;
    }

    /// <summary>The lighting material (bind plugin output facades here).</summary>
    public GraphicsMaterial Material { get; }

    /// <summary>The G-buffer resource read by the lighting pass.</summary>
    public RenderGraphTexture GBuffer { get; }

    /// <summary>The scene color resource written by the lighting pass.</summary>
    public RenderGraphTexture SceneColor { get; }

    /// <summary>The shadow map resource, or null when the pass has no shadow input.</summary>
    public RenderGraphTexture? ShadowMap { get; set; }

    /// <summary>Whether the pass reads <see cref="ShadowMap"/> this frame. While false,
    /// no shadow map read is declared — which is what graph-culls the shadow pass.</summary>
    public bool ShadowMapEnabled { get; set; } = true;

    /// <summary>The AO plugin output read by the pass, or null (the material's AO
    /// parameter should then stay bound to a neutral texture).</summary>
    public RenderGraphTexture? AoInput { get; set; }

    /// <summary>The diffuse GI plugin output read by the pass, or null.</summary>
    public RenderGraphTexture? GiDiffuseInput { get; set; }

    /// <summary>The specular GI plugin output read by the pass, or null.</summary>
    public RenderGraphTexture? GiSpecularInput { get; set; }

    /// <summary>Additional resources the pass reads, so their producers survive
    /// culling. Use this for custom plugins the node does not know about.</summary>
    public List<RenderGraphTexture> ExtraReads { get; } = new();

    /// <summary>
    /// Assembles and uploads the per-frame lighting data. Invoked at the start of
    /// <see cref="Execute"/>, before the pass is recorded. Throw
    /// <see cref="InvalidOperationException"/> here when required per-frame input
    /// (e.g. a camera) is missing.
    /// </summary>
    public Action<RGNode_DeferredLighting>? PrepareData { get; set; }

    /// <summary>Optional CPU/GPU stage instrumentation.</summary>
    public PassInstrumentation? Instrumentation { get; set; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.Read(GBuffer);
        if (ShadowMapEnabled && ShadowMap != null)
        {
            builder.Read(ShadowMap);
        }
        if (AoInput != null)
        {
            builder.Read(AoInput);
        }
        if (GiDiffuseInput != null)
        {
            builder.Read(GiDiffuseInput);
        }
        if (GiSpecularInput != null)
        {
            builder.Read(GiSpecularInput);
        }
        List<RenderGraphTexture> extraReads = ExtraReads;
        for (int i = 0; i < extraReads.Count; i++)
        {
            builder.Read(extraReads[i]);
        }
        builder.Write(SceneColor);
        if (!_graph.HasDestinationThisFrame)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        // Uniform uploads must precede the pass recording: buffer writes are
        // queue-side operations outside the recorded command stream, so a buffer
        // rewritten after recording would leak the newer value into this pass.
        PrepareData?.Invoke(this);

        RenderPassScope pass = Instrumentation != null
            ? Instrumentation.BeginPass(context.RenderContext, SceneColor.Texture.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty)
            : context.RenderContext.BeginPass(SceneColor.Texture.FrameBuffer);
        using (pass)
        {
            pass.Draw(_fullScreenMesh, Material);
            Instrumentation?.ScheduleResolve(pass);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { }
}
