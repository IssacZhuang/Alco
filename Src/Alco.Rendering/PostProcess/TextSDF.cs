using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Generates signed distance fields (SDF) from regular font atlas textures using compute shaders.
/// Uses a flood-fill algorithm similar to lighting propagation but with linear distance attenuation.
/// </summary>
public class TextSDF : AutoDisposable
{
    private readonly ComputeMaterial _material;
    private readonly float _maxDistance;

    /// <summary>
    /// Creates a new TextSDF compute processor.
    /// </summary>
    /// <param name="material">The GenerateSDF compute material</param>
    /// <param name="maxDistance">Maximum SDF distance in pixels (typically 6-12)</param>
    internal TextSDF(ComputeMaterial material, float maxDistance = 6.0f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDistance);
        
        _material = material.CreateInstance();
        _maxDistance = maxDistance;
    }

    /// <summary>
    /// Generates SDF from a regular font atlas texture.
    /// </summary>
    /// <param name="computePass">GPU compute pass</param>
    /// <param name="input">Input font atlas texture (single channel, glyph=1.0, background=0.0)</param>
    /// <param name="output">Output SDF texture (same dimensions as input)</param>
    /// <param name="numPasses">Number of flood-fill passes (should match maxDistance)</param>
    public void Generate(GPUCommandBuffer.ComputePass computePass, RenderTexture input, RenderTexture output, int numPasses = 6)
    {
        if (input.Width != output.Width || input.Height != output.Height)
        {
            throw new ArgumentException("Input and output textures must have the same dimensions");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numPasses);

        // For ping-pong buffering, we'll assume the caller provides both input and output
        // textures and we'll swap between them

        // Set up constant data
        var constant = new TextSdfConstant
        {
            attenuation = 1.0f / _maxDistance  // Normalize attenuation
        };

        RenderTexture frontBuffer = input;
        RenderTexture backBuffer = output;

        // Perform flood-fill passes
        for (int pass = 0; pass < numPasses; pass++)
        {
            // Set textures for this pass
            _material.TrySetRenderTexture(ShaderResourceId.FrontBuffer, frontBuffer);
            _material.TrySetRenderTexture(ShaderResourceId.BackBuffer, backBuffer);

            // Dispatch compute shader
            _material.DispatchBySizeWithConstant(
                computePass,
                input.Width,
                input.Height,
                1,
                constant
            );

            // Swap buffers for next pass (ping-pong)
            if (pass < numPasses - 1)
            {
                (frontBuffer, backBuffer) = (backBuffer, frontBuffer);
            }
        }

        // Final result should be in the output texture
    }

    /// <summary>
    /// Generates SDF with automatic pass calculation based on max distance.
    /// </summary>
    /// <param name="computePass">GPU compute pass</param>
    /// <param name="input">Input font atlas texture</param>
    /// <param name="output">Output SDF texture</param>
    public void Generate(GPUCommandBuffer.ComputePass computePass, RenderTexture input, RenderTexture output)
    {
        // Use max distance as number of passes (each pass propagates ~1 pixel)
        int numPasses = (int)Math.Ceiling(_maxDistance);
        Generate(computePass, input, output, numPasses);
    }

    protected override void Dispose(bool disposing)
    {
        // ComputeMaterial instances are managed by the rendering system
        // No manual disposal needed like GaussianBlur
    }
}