
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A render pipeline drives the complete frame of one view: it owns the scene render
/// texture that world and UI rendering draw into, and a <see cref="PostProcessChain"/>
/// that resolves the scene texture into the final destination (typically the swapchain).
/// Post-processing is therefore a property of the pipeline, not a global engine concern —
/// each view has its own pipeline with its own effect settings.
/// <br/>The pipeline is a plain object, created and driven manually by its owner
/// (the engine for the main view, game code for additional views):
/// <list type="number">
/// <item><see cref="BeginFrame"/>: clears the scene texture (<see cref="ClearScene"/>).</item>
/// <item>The owner renders the scene into <see cref="SceneFrameBuffer"/>.</item>
/// <item><see cref="RenderFrame"/>: <see cref="RenderScene"/> (pipeline-internal passes),
/// then the post-process chain writes the final image into the destination.</item>
/// <item><see cref="Resize"/> when the view size changes.</item>
/// </list>
/// </summary>
public abstract class RenderPipeline : AutoDisposable, IRenderTarget
{
    private readonly RenderingSystem _rendering;
    private readonly GPUAttachmentLayout _sceneLayout;
    private readonly GPUCommandBuffer _clearCommand;
    private RenderTexture? _sceneRenderTexture;

    /// <summary>
    /// The rendering system.
    /// </summary>
    protected RenderingSystem Rendering => _rendering;

    /// <summary>
    /// The post-process chain executed between the scene texture and the final destination.
    /// </summary>
    public PostProcessChain PostProcess { get; }

    /// <summary>
    /// The scene render texture. Recreated when the pipeline is resized.
    /// </summary>
    public RenderTexture SceneRenderTexture => _sceneRenderTexture!;

    /// <summary>
    /// The frame buffer of the scene render texture — the target for scene and UI rendering.
    /// </summary>
    public GPUFrameBuffer SceneFrameBuffer => SceneRenderTexture.FrameBuffer;

    /// <inheritdoc cref="IRenderTarget.RenderTexture" />
    public RenderTexture RenderTexture => SceneRenderTexture;

    /// <summary>
    /// Whether this pipeline owns <see cref="SceneRenderTexture"/> and disposes it. Pipelines
    /// whose scene texture is owned by an internal renderer (e.g. a deferred pipeline's
    /// forward target) override this to false.
    /// </summary>
    protected virtual bool OwnsSceneRenderTexture => true;

    /// <summary>
    /// Creates the pipeline. The scene render texture is assigned by the concrete
    /// pipeline via <see cref="SetSceneRenderTexture"/> during construction.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="sceneLayout">The attachment layout of the scene render texture, used by
    /// the default <see cref="CreateSceneRenderTexture"/> implementation.</param>
    /// <param name="blitShader">The shader the post-process chain uses for plain copies.</param>
    protected RenderPipeline(RenderingSystem rendering, GPUAttachmentLayout sceneLayout, Shader blitShader)
    {
        _rendering = rendering;
        _sceneLayout = sceneLayout;

        _clearCommand = _rendering.GraphicsDevice.CreateCommandBuffer();
        PostProcess = new PostProcessChain(_rendering, blitShader);
    }

    /// <summary>
    /// Prepares the scene texture for a new frame. The default clears it.
    /// </summary>
    public void BeginFrame()
    {
        ClearScene();
    }

    /// <summary>
    /// Runs the pipeline-internal scene passes (<see cref="RenderScene"/>) and resolves the
    /// scene texture through the post-process chain into <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The final output frame buffer (e.g. the swapchain frame
    /// buffer). When null, the scene passes still run but the final output is skipped
    /// (minimized or headless view).</param>
    public void RenderFrame(GPUFrameBuffer? destination)
    {
        RenderScene();

        if (destination == null)
        {
            return;
        }

        PostProcess.Execute(SceneRenderTexture, destination);
    }

    /// <summary>
    /// Recreates the scene render texture and the resolution-dependent post-process
    /// resources at a new size.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        ResizeScene(width, height);
        PostProcess.Resize(width, height);
    }

    /// <summary>
    /// Creates the scene render texture. The default implementation creates a render texture
    /// with the layout passed to the constructor.
    /// </summary>
    protected virtual RenderTexture CreateSceneRenderTexture(uint width, uint height)
    {
        return _rendering.CreateRenderTexture(_sceneLayout, width, height, "pipeline_scene");
    }

    /// <summary>
    /// Recreates the scene render texture at a new size. The default implementation disposes
    /// the current texture and creates a new one.
    /// </summary>
    protected virtual void ResizeScene(uint width, uint height)
    {
        SceneRenderTexture.Dispose();
        SetSceneRenderTexture(CreateSceneRenderTexture(width, height));
    }

    /// <summary>
    /// Assigns the scene render texture. Concrete pipelines call this during construction
    /// and from <see cref="ResizeScene"/> overrides.
    /// </summary>
    protected void SetSceneRenderTexture(RenderTexture renderTexture)
    {
        _sceneRenderTexture = renderTexture;
    }

    /// <summary>
    /// Clears the scene texture at the beginning of the frame. The default clears to black
    /// with depth 1 and stencil 0.
    /// </summary>
    protected virtual void ClearScene()
    {
        _clearCommand.Begin();
        using (_clearCommand.BeginRender(SceneRenderTexture.FrameBuffer, ColorFloat.Black, 1f, 0))
        {
        }
        _clearCommand.End();
        _rendering.GraphicsDevice.Submit(_clearCommand);
    }

    /// <summary>
    /// Pipeline-internal scene rendering, invoked by <see cref="RenderFrame"/> before the
    /// post-process chain. Pipelines that drive their own passes (e.g. deferred) override
    /// this; pipelines whose scene is drawn externally leave it empty.
    /// </summary>
    protected virtual void RenderScene()
    {
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (OwnsSceneRenderTexture)
            {
                _sceneRenderTexture?.Dispose();
            }

            PostProcess.Dispose();
            _clearCommand.Dispose();
        }
    }
}
