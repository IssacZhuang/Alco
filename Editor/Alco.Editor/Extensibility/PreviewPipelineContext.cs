using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The construction data handed to an <see cref="IPreviewPipelineFactory"/> by the
/// <see cref="PreviewViewport"/>: the shared editor services, the resource base name,
/// the initial render size, and the viewport-owned content nodes.
/// <br/>The <see cref="RecordNode"/> is created up front (its callback reads the
/// viewport's RecordFrame delegate). The scene content node is created through
/// <see cref="CreateSceneNode"/> once the pipeline exists, because its base class
/// (<see cref="RGNode_SceneContent"/>) binds the pipeline's graph and chain at
/// construction; its render override reads the viewport's SceneContent delegate.
/// </summary>
public sealed class PreviewPipelineContext
{
    /// <summary>Creates the context.</summary>
    /// <param name="editor">The shared editor services.</param>
    /// <param name="name">The base name for the pipeline and its resources.</param>
    /// <param name="width">The initial render width in pixels.</param>
    /// <param name="height">The initial render height in pixels.</param>
    /// <param name="recordNode">The node recording per-frame GPU work ahead of the scene pass.</param>
    /// <param name="createSceneNode">Creates the scene content node once the pipeline's graph and chain exist.</param>
    public PreviewPipelineContext(
        EditorContext editor,
        string name,
        int width,
        int height,
        RGNode_Callback recordNode,
        Func<RenderGraph, RenderChain, RGNode_SceneContent> createSceneNode)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(recordNode);
        ArgumentNullException.ThrowIfNull(createSceneNode);
        Editor = editor;
        Name = name;
        Width = width;
        Height = height;
        RecordNode = recordNode;
        CreateSceneNode = createSceneNode;
    }

    /// <summary>The shared editor services.</summary>
    public EditorContext Editor { get; }

    /// <summary>The base name for the pipeline and its resources.</summary>
    public string Name { get; }

    /// <summary>The initial render width in pixels.</summary>
    public int Width { get; }

    /// <summary>The initial render height in pixels.</summary>
    public int Height { get; }

    /// <summary>The viewport-owned node recording per-frame GPU work ahead of the scene pass.</summary>
    public RGNode_Callback RecordNode { get; }

    /// <summary>Creates the viewport-owned scene content node for the pipeline's graph and chain.</summary>
    public Func<RenderGraph, RenderChain, RGNode_SceneContent> CreateSceneNode { get; }

    /// <summary>
    /// Creates the default display-transform node with every built-in operator shader
    /// wired up (Linear through AgX). The node starts on its own default operator;
    /// the caller sets <see cref="RGNode_Tonemap.Operator"/> before using it.
    /// </summary>
    /// <param name="graph">The pipeline's render graph.</param>
    /// <param name="chain">The pipeline's content chain.</param>
    /// <param name="postProcessLayout">The chain's color-only attachment layout.</param>
    public RGNode_Tonemap CreateDefaultTonemap(RenderGraph graph, RenderChain chain, GPUAttachmentLayout postProcessLayout)
    {
        RenderingSystem rendering = Editor.RenderingSystem;
        BuiltInAssets builtIn = Editor.Engine.BuiltInAssets;
        return new RGNode_Tonemap(
            rendering,
            graph,
            chain,
            postProcessLayout,
            new RGNode_Tonemap.Descriptor
            {
                BlitShader = builtIn.Shader_Blit,
                ReinhardShader = builtIn.Shader_ReinhardLuminanceTonemap,
                Uncharted2Shader = builtIn.Shader_Uncharted2Tonemap,
                FilmicShader = builtIn.Shader_FilmicTonemap,
                AcesShader = builtIn.Shader_AcesTonemap,
                NeutralShader = builtIn.Shader_NeutralTonemap,
                AgxShader = builtIn.Shader_AgxTonemap,
            });
    }
}
