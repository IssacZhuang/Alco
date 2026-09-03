using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// Creates the render pipeline behind a <see cref="PreviewViewport"/>: the HDR scene
/// pass, the per-frame record and scene-content nodes, an optional display-transform
/// node and the RGBA8 target shown through <c>ImGui.Image</c>.
/// </summary>
public interface IPreviewPipelineFactory
{
    /// <summary>Creates the pipeline, its target and its optional tonemap node.</summary>
    /// <param name="context">The pipeline construction data (owner nodes included).</param>
    PreviewPipeline Create(PreviewPipelineContext context);
}
