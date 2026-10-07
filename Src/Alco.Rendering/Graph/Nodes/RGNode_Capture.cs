using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A render graph capture node: when armed, copies the chain's current color at this
/// node's graph position into a private RGBA8 render texture, which an external
/// service (typically <c>RenderCaptureSystem</c>) reads back after the frame. Where
/// the node is registered defines what is captured — right after the content node is
/// the raw scene, between post-process nodes is a partially processed image, and
/// before the final blit is the complete frame.
/// <br/>The node is disabled (and skipped by the graph entirely) until
/// <see cref="Submit"/> arms it; an armed capture survives culling via
/// <see cref="RenderGraphBuilder.ProducesOutput"/> because the capture texture is not
/// a graph resource. Captures also run on headless frames (null destination), where
/// the final blit disables itself.
/// <br/>One capture at a time: arm, take the completion, read back, then arm again.
/// </summary>
public sealed class RGNode_Capture : AutoDisposable, IRenderGraphNode
{
    private readonly RenderChain _chain;
    private readonly Mesh _fullScreenMesh;
    private readonly GraphicsMaterial _blitMaterial;
    private readonly RenderTexture _captureTexture;

    // The resource to capture, taken during Setup: by the time this node runs its
    // Setup (registration order), the chain's Current is the content produced so far
    // at this node's position.
    private RenderGraphTexture? _input;
    private bool _armed;
    private bool _completed;

    /// <summary>
    /// Creates the capture node, including its blit material and capture texture.
    /// </summary>
    /// <param name="rendering">The rendering system, for GPU resources.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node captures from.</param>
    /// <param name="blitShader">The plain-copy shader, typically the owning pipeline's
    /// <c>BlitShader</c>.</param>
    public RGNode_Capture(RenderingSystem rendering, RenderGraph graph, RenderChain chain, Shader blitShader)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(blitShader);
        _chain = chain;
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateGraphicsMaterial(blitShader);
        _captureTexture = rendering.CreateRenderTexture(
            rendering.PreferredRGBATexturePass, graph.Width, graph.Height, "render_graph_capture");
    }

    /// <summary>
    /// The capture target an armed request's pixels land in: RGBA8 at the graph's
    /// viewport size, with a stable facade identity across resizes. Read (and read
    /// back) only between <see cref="TryTakeCompleted"/> returning true and the next
    /// <see cref="Submit"/>.
    /// </summary>
    public RenderTexture CaptureTexture => _captureTexture;

    /// <summary>Whether a capture is armed and has not rendered yet.</summary>
    public bool HasPendingRequest => _armed;

    /// <inheritdoc />
    public bool IsEnabled => _armed;

    /// <summary>
    /// Arms one capture: the node's next executed frame copies the chain content at
    /// its registered position into <see cref="CaptureTexture"/>. Must be called on
    /// the main thread outside a frame, and only while no capture is pending.
    /// </summary>
    /// <exception cref="InvalidOperationException">A capture is already armed.</exception>
    public void Submit()
    {
        if (_armed)
        {
            throw new InvalidOperationException("The capture node already has a pending capture.");
        }

        _armed = true;
    }

    /// <summary>
    /// Takes an armed capture's completion: returns true exactly once per
    /// <see cref="Submit"/>, after the node's Execute copied the frame into
    /// <see cref="CaptureTexture"/>.
    /// </summary>
    public bool TryTakeCompleted()
    {
        if (!_completed)
        {
            return false;
        }

        _completed = false;
        return true;
    }

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _input = _chain.Current!;
        builder.Read(_input);
        builder.ProducesOutput();
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, _input!.Texture);
        using (RenderPassScope pass = context.RenderContext.BeginPass(_captureTexture.FrameBuffer))
        {
            pass.Draw(_fullScreenMesh, _blitMaterial);
        }

        _armed = false;
        _completed = true;
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        _captureTexture.Resize(width, height);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blitMaterial.Dispose();
            _captureTexture.Dispose();
        }
    }
}
