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