namespace Alco.Rendering;

/// <summary>
/// Texture processor creation for bloom, FXAA, blur and SDF effects.
/// </summary>
public partial class RenderingSystem
{
    /// <summary>
    /// Creates a sum-of-gaussians bloom processor.
    /// </summary>
    /// <param name="blitShader">The additive composite shader.</param>
    /// <param name="setupShader">The threshold setup shader.</param>
    /// <param name="downsampleShader">The downsample chain shader.</param>
    /// <param name="gaussianShader">The separable gaussian stage shader.</param>
    /// <returns>A new bloom processor instance.</returns>
    public Bloom CreateBloom(Shader blitShader, Shader setupShader, Shader downsampleShader, Shader gaussianShader)
    {
        return new Bloom(this, blitShader, setupShader, downsampleShader, gaussianShader);
    }

    /// <summary>
    /// Creates a separable gaussian blur processor.
    /// </summary>
    /// <param name="material">The compute material used for the blur passes.</param>
    /// <param name="kernelSizeX">The horizontal kernel size in pixels.</param>
    /// <param name="kernelSizeY">The vertical kernel size in pixels.</param>
    /// <param name="kernel">The kernel weights.</param>
    /// <returns>A new gaussian blur processor instance.</returns>
    public GaussianBlur CreateGaussianBlur(ComputeMaterial material, int kernelSizeX, int kernelSizeY, ReadOnlySpan<float> kernel)
    {
        return new GaussianBlur(this, material, kernelSizeX, kernelSizeY, kernel);
    }

    /// <summary>
    /// Creates a new TextSDF processor for generating signed distance fields from font atlases.
    /// </summary>
    /// <param name="material">The GenerateSDF compute material</param>
    /// <param name="maxDistance">Maximum SDF distance in pixels (typically 6-12)</param>
    /// <returns>A new TextSDF processor instance</returns>
    public TextSDF CreateTextSDF(ComputeMaterial material, float maxDistance = 6.0f)
    {
        return new TextSDF(material, maxDistance);
    }

    /// <summary>
    /// Creates a new FXAA (Fast Approximate Anti-Aliasing) post-processing effect
    /// from the fxaa module: each (quality, mode) pair resolves as its own
    /// generic value specialization (MainPS&lt;let Quality : int,
    /// let Mode : int&gt;).
    /// </summary>
    /// <param name="blitShader">The blit shader to use for copying the result to the final target</param>
    /// <param name="fxaaModule">The fxaa module library.</param>
    /// <returns>A new FXAA post-processing effect instance</returns>
    public FXAA CreateFXAA(Shader blitShader, Shader fxaaShader)
    {
        return new FXAA(this, blitShader, fxaaShader);
    }
}
