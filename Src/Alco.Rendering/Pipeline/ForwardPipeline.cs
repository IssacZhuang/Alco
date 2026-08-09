
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The forward pipeline: a flat <see cref="RenderNodeChain"/> executed against a pipeline-owned
/// scene texture. Content nodes (world, UI, ...) draw into the scene texture; content
/// processor nodes (color grading, bloom, tone mapping, ...) transform it through the
/// chain's ping-pong temporaries; the chain blits the final image into the destination.
/// The owner composes the frame purely by ordering nodes via <see cref="Use"/> — there
/// is no separate post-processing concept.
/// <br/>The pipeline is a plain object, created and driven manually by its owner (the
/// engine for the main view, game code for additional views):
/// <list type="number">
/// <item><see cref="Use"/> the nodes in the order they should execute.</item>
/// <item><see cref="Render"/>: clears the scene texture, then runs the chain into the
/// final destination (typically the swapchain frame buffer).</item>
/// <item><see cref="Resize"/> when the view size changes.</item>
/// </list>
/// </summary>
public sealed class ForwardPipeline : AutoDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly GPUAttachmentLayout _sceneLayout;
    private readonly GPUCommandBuffer _clearCommand;
    private readonly RenderNodeChain _chain;
    private RenderTexture _sceneRenderTexture;

    /// <summary>
    /// The scene render texture the chain's content nodes draw into (via the chain, never
    /// directly). Resized in place when the pipeline is resized; the object identity
    /// stays stable across resizes.
    /// </summary>
    public RenderTexture SceneTexture => _sceneRenderTexture;

    /// <summary>
    /// The color the scene texture is cleared to at the start of <see cref="Render"/>.
    /// Depth and stencil are always cleared to 1 and 0.
    /// </summary>
    public ColorFloat ClearColor { get; set; } = ColorFloat.Black;

    /// <summary>
    /// Creates a forward pipeline with its scene render texture.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="sceneLayout">The attachment layout of the scene render texture
    /// (e.g. <see cref="RenderingSystem.PreferredSDRPass"/> or
    /// <see cref="RenderingSystem.PreferredHDRPass"/> for HDR content processors).</param>
    /// <param name="blitShader">The shader the node chain uses for plain copies.</param>
    /// <param name="width">The initial scene texture width in pixels.</param>
    /// <param name="height">The initial scene texture height in pixels.</param>
    public ForwardPipeline(RenderingSystem rendering, GPUAttachmentLayout sceneLayout, Shader blitShader, uint width, uint height)
    {
        _rendering = rendering;
        _sceneLayout = sceneLayout;
        _clearCommand = _rendering.GraphicsDevice.CreateCommandBuffer();
        _chain = new RenderNodeChain(_rendering, blitShader);
        _sceneRenderTexture = CreateSceneRenderTexture(width, height);
    }

    /// <summary>
    /// Registers a forward render node at the end of the chain. The pipeline takes
    /// ownership and disposes the node (when <see cref="System.IDisposable"/>) with itself.
    /// </summary>
    public void Use(IRenderNode node)
    {
        _chain.Use(node);
    }

    /// <summary>
    /// Removes a node previously added via <see cref="Use"/>. The node is not disposed.
    /// </summary>
    public bool Remove(IRenderNode node)
    {
        return _chain.Remove(node);
    }

    /// <summary>
    /// Gets the first node of the given type, or null when the chain has none.
    /// </summary>
    public T? Get<T>() where T : class, IRenderNode
    {
        return _chain.Get<T>();
    }

    /// <summary>
    /// The registered nodes, in execution order.
    /// </summary>
    public IReadOnlyList<IRenderNode> Nodes => _chain.Nodes;

    /// <summary>
    /// Renders the frame: clears the scene texture, then executes the node chain —
    /// content nodes draw into the scene texture, content processors transform it, the
    /// final image lands in <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The final output frame buffer (e.g. the swapchain frame
    /// buffer). When null, content nodes still render into the scene texture and all
    /// processors are skipped (minimized or headless view).</param>
    public void Render(GPUFrameBuffer? destination)
    {
        _clearCommand.Begin();
        using (_clearCommand.BeginRender(_sceneRenderTexture.FrameBuffer, ClearColor, 1f, 0))
        {
        }
        _clearCommand.End();
        _rendering.GraphicsDevice.Submit(_clearCommand);

        _chain.Execute(_sceneRenderTexture, destination);
    }

    /// <summary>
    /// Resizes the scene render texture in place and notifies the node chain. The scene
    /// texture keeps its object identity, so materials referencing it need no rebinding.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        _sceneRenderTexture.Resize(width, height);
        _chain.Resize(width, height);
    }

    private RenderTexture CreateSceneRenderTexture(uint width, uint height)
    {
        return _rendering.CreateRenderTexture(_sceneLayout, width, height, "pipeline_scene");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _chain.Dispose();
            _sceneRenderTexture.Dispose();
            _clearCommand.Dispose();
        }
    }
}
