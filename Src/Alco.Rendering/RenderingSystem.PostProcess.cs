namespace Alco.Rendering;

// post process factory

public partial class RenderingSystem
{
    public Bloom CreateBloom(Shader blitShader, Shader clampShader, Shader downSampleShader, Shader upSampleShader, uint targetDownSampleHeight)
    {
        return new Bloom(this, blitShader, clampShader, downSampleShader, upSampleShader, targetDownSampleHeight);
    }

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
    /// Creates a new FXAA (Fast Approximate Anti-Aliasing) post-processing effect.
    /// </summary>
    /// <param name="fxaaShader">The FXAA shader to use for the effect</param>
    /// <param name="blitShader">The blit shader to use for copying the result to the final target</param>
    /// <returns>A new FXAA post-processing effect instance</returns>
    public FXAA CreateFXAA(Shader fxaaShader, Shader blitShader)
    {
        return new FXAA(this, fxaaShader, blitShader);
    }
}