using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The built-in <see cref="IPreviewPipelineFactory"/>: an HDR single-slot scene
/// pipeline, the viewport's record and scene-content nodes, the default tonemap node
/// (Neutral operator — the game's post chain) and an RGBA8 target. Stateless; use
/// <see cref="Instance"/>.
/// </summary>
public sealed class DefaultPreviewPipelineFactory : IPreviewPipelineFactory
{
    private DefaultPreviewPipelineFactory()
    {
    }

    /// <summary>The shared instance.</summary>
    public static DefaultPreviewPipelineFactory Instance { get; } = new();

    /// <inheritdoc />
    public PreviewPipeline Create(PreviewPipelineContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        RenderingSystem rendering = context.Editor.RenderingSystem;

        var pipeline = new RenderPipeline(rendering, new RenderPipeline.Descriptor
        {
            SceneLayout = rendering.PreferredHDRPass,
            BlitShader = context.Editor.Engine.BuiltInAssets.Shader_Blit,
            Width = (uint)context.Width,
            Height = (uint)context.Height,
            Name = context.Name,
        });
        pipeline.Use(context.RecordNode);
        pipeline.Use(context.CreateSceneNode(pipeline.Graph, pipeline.Chain));

        RGNode_Tonemap tonemap = context.CreateDefaultTonemap(pipeline.Graph, pipeline.Chain, pipeline.PostProcessLayout);
        // Neutral with its default data is the game's post chain (Engine.cs uses
        // the same operator), so the preview shows authored colors the way they
        // present in game. The toolbar can switch operators for comparison.
        tonemap.Operator = TonemapType.Neutral;
        pipeline.Use(tonemap);

        RenderTexture target = rendering.CreateRenderTexture(
            rendering.PreferredRGBATexturePass, (uint)context.Width, (uint)context.Height, context.Name + "_target");
        return new PreviewPipeline(pipeline, target, tonemap);
    }
}
