using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The product of an <see cref="IPreviewPipelineFactory"/>: the pipeline the viewport
/// drives, the RGBA8 target it blits into, and the display-transform node (null when
/// the pipeline has none, in which case the viewport hides the operator switcher).
/// </summary>
/// <param name="Pipeline">The render pipeline the viewport renders per frame.</param>
/// <param name="Target">The render target the pipeline draws into (shown via ImGui).</param>
/// <param name="Tonemap">The display-transform node, or null for pipelines without one.</param>
public sealed record PreviewPipeline(RenderPipeline Pipeline, RenderTexture Target, RGNode_Tonemap? Tonemap);
