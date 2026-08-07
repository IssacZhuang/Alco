
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The default forward pipeline: the owner renders sprites and UI directly into the scene
/// texture; the pipeline clears it in <see cref="RenderPipeline.BeginFrame"/> and resolves
/// it through the post-process chain into the final destination in
/// <see cref="RenderPipeline.RenderFrame"/>.
/// </summary>
public sealed class ForwardPipeline : RenderPipeline
{
    /// <summary>
    /// Creates a forward pipeline with its scene render texture.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="sceneLayout">The attachment layout of the scene render texture
    /// (e.g. <see cref="RenderingSystem.PreferredSDRPass"/> or
    /// <see cref="RenderingSystem.PreferredHDRPass"/> for HDR post-processing).</param>
    /// <param name="blitShader">The shader the post-process chain uses for plain copies.</param>
    /// <param name="width">The initial scene texture width in pixels.</param>
    /// <param name="height">The initial scene texture height in pixels.</param>
    public ForwardPipeline(RenderingSystem rendering, GPUAttachmentLayout sceneLayout, Shader blitShader, uint width, uint height)
        : base(rendering, sceneLayout, blitShader)
    {
        SetSceneRenderTexture(CreateSceneRenderTexture(width, height));
    }
}
