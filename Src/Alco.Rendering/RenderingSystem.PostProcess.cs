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
}