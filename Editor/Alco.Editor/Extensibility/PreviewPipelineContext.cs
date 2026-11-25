using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The construction data handed to an <see cref="IPreviewPipelineFactory"/> by
/// <see cref="PreviewViewport"/>: the shared editor services, resource base name,
/// initial extent, and per-frame recording delegates.
/// </summary>
public sealed class PreviewPipelineContext
{
    private readonly Action<RenderGraphContext> _recordFrame;
    private readonly Action<RenderContext, RenderContext.RenderScope> _sceneContent;

    /// <summary>Creates the preview pipeline construction context.</summary>
    /// <param name="editor">The shared editor services.</param>
    /// <param name="name">The base name for the graph and its resources.</param>
    /// <param name="width">The initial render width in pixels.</param>
    /// <param name="height">The initial render height in pixels.</param>
    /// <param name="recordFrame">Records GPU compute/upload work ahead of the scene pass.</param>
    /// <param name="sceneContent">Records graphics draw commands into the scene render pass.</param>
    public PreviewPipelineContext(
        EditorContext editor,
        string name,
        int width,
        int height,
        Action<RenderGraphContext> recordFrame,
        Action<RenderContext, RenderContext.RenderScope> sceneContent)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(recordFrame);
        ArgumentNullException.ThrowIfNull(sceneContent);

        Editor = editor;
        Name = name;
        Width = Math.Max(width, 1);
        Height = Math.Max(height, 1);
        _recordFrame = recordFrame;
        _sceneContent = sceneContent;
    }

    /// <summary>The shared editor services.</summary>
    public EditorContext Editor { get; }

    /// <summary>The base name for the pipeline and its resources.</summary>
    public string Name { get; }

    /// <summary>The initial render width in pixels.</summary>
    public int Width { get; }

    /// <summary>The initial render height in pixels.</summary>
    public int Height { get; }

    /// <summary>Records per-frame scene draw commands inside an active render pass.</summary>
    /// <param name="renderContext">The active frame render context.</param>
    /// <param name="scope">The open render pass scope.</param>
    public void InvokeSceneContent(RenderContext renderContext, in RenderContext.RenderScope scope)
    {
        _sceneContent(renderContext, scope);
    }

    /// <summary>Creates the viewport's pre-scene record node (e.g., GPU particle simulation).</summary>
    public IRenderGraphNode CreateRecordNode() => new RecordCallbackNode(_recordFrame, Name + "_Record");

    /// <summary>
    /// Creates the default display-transform node reading <paramref name="chain"/>'s current
    /// HDR texture and writing directly to <see cref="RenderGraphBuilder.Output"/>.
    /// </summary>
    /// <param name="chain">The pipeline's HDR content chain.</param>
    public RGNode_Tonemap CreateDefaultTonemap(RenderChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        RenderingSystem rendering = Editor.RenderingSystem;
        return new RGNode_Tonemap(
            rendering,
            _ => chain.Current,
            outputProvider: null,
            rendering.Shaders,
            Name + "_Tonemap")
        {
            Operator = TonemapType.Neutral,
        };
    }

    private sealed class RecordCallbackNode(Action<RenderGraphContext> callback, string name) : RenderGraphNode(name)
    {
        protected override void SetupPass(RenderGraphBuilder builder, RenderGraphPassBuilder pass)
        {
            pass.ProducesOutput();
        }

        protected override void ExecutePass(in RenderGraphContext context)
        {
            callback(context);
        }
    }
}
