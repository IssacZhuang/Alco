using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A chain transform node that draws a full-screen material: reads the chain's
/// current content, draws into its own output transient and advances the chain — the
/// typical post-process effect shape (tone mapping, FXAA, color grading, ...).
/// <br/>The chain input facade is bound to the material per frame (toggling an
/// earlier chain node changes this node's input resource; the facade version check
/// keeps steady-state rebinding free). Additional static bindings (G-buffer textures,
/// data buffers, ...) are set through <see cref="BindTexture"/> /
/// <see cref="BindTextureDepth"/> / <see cref="BindBuffer"/>.
/// <br/>The node owns its render context; it does not own the material.
/// </summary>
public class FullscreenPassNode : ChainTransformNode
{
    private readonly Material _material;
    private readonly string _inputBinding;
    private readonly bool _inputIsDepth;
    private readonly RenderContext _context;
    private readonly Mesh _fullScreenMesh;

    /// <summary>
    /// Creates the node.
    /// </summary>
    /// <param name="rendering">The rendering system, for the render context and the
    /// full-screen mesh.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node reads and advances.</param>
    /// <param name="material">The full-screen material. Not owned by the node.</param>
    /// <param name="outputLayout">The attachment layout of the node's output transient.</param>
    /// <param name="inputBinding">The material parameter the chain input facade is
    /// bound to.</param>
    /// <param name="inputIsDepth">Bind the chain input's depth attachment instead of
    /// its color attachment.</param>
    /// <param name="resolutionScale">The output transient's resolution scale relative
    /// to the graph viewport.</param>
    /// <param name="name">A diagnostic name.</param>
    public FullscreenPassNode(RenderingSystem rendering, RenderGraph graph, RenderChain chain,
        Material material, GPUAttachmentLayout outputLayout,
        string inputBinding = ShaderResourceId.Texture, bool inputIsDepth = false,
        float resolutionScale = 1.0f, string name = "fullscreen_pass")
        : base(graph, chain, outputLayout, resolutionScale, name)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(material);
        _material = material;
        _inputBinding = inputBinding;
        _inputIsDepth = inputIsDepth;
        _context = rendering.CreateRenderContext(name);
        _fullScreenMesh = rendering.MeshFullScreen;
    }

    /// <summary>The full-screen material.</summary>
    public Material Material => _material;

    /// <summary>Optional CPU/GPU stage instrumentation.</summary>
    public PassInstrumentation? Instrumentation { get; set; }

    /// <summary>Binds a resource's facade to a material parameter (color attachment
    /// <paramref name="attachmentIndex"/>). Stable across frames.</summary>
    public void BindTexture(string name, RenderGraphTexture resource, int attachmentIndex = 0)
    {
        _material.SetRenderTexture(name, resource.Texture, attachmentIndex);
    }

    /// <summary>Binds a resource's depth attachment to a material parameter. Stable
    /// across frames.</summary>
    public void BindTextureDepth(string name, RenderGraphTexture resource)
    {
        _material.SetRenderTextureDepth(name, resource.Texture);
    }

    /// <summary>Binds a GPU buffer to a material parameter.</summary>
    public void BindBuffer(string name, GraphicsBuffer buffer)
    {
        _material.SetBuffer(name, buffer);
    }

    /// <inheritdoc />
    protected override void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context)
    {
        if (_inputIsDepth)
        {
            _material.SetRenderTextureDepth(_inputBinding, input);
        }
        else
        {
            _material.SetRenderTexture(_inputBinding, input);
        }

        long startTicks = Instrumentation?.BeginCpuTiming() ?? 0;
        if (Instrumentation != null)
        {
            Instrumentation.BeginPass(_context, output.FrameBuffer, ReadOnlySpan<ClearColorData>.Empty);
            _context.Draw(_fullScreenMesh, _material);
            Instrumentation.EndPass(_context);
            Instrumentation.PushCpuTiming(startTicks);
        }
        else
        {
            _context.Begin(output.FrameBuffer);
            _context.Draw(_fullScreenMesh, _material);
            _context.End();
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _context.Dispose();
        }
        base.Dispose(disposing);
    }
}
