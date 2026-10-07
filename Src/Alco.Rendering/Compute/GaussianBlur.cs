
using Alco.Graphics;

namespace Alco.Rendering;

public class GaussianBlur : AutoDisposable
{
    private readonly ComputeMaterial _material;

    private readonly int _kernelSizeX;
    private readonly int _kernelSizeY;
    private readonly GraphicsArrayBuffer<float> _kernelBuffer;
    private float _kernelSum;

    internal GaussianBlur(
        RenderingSystem renderingSystem,
        ComputeMaterial material,
        int kernelSizeX,
        int kernelSizeY,
        ReadOnlySpan<float> kernel
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(kernelSizeX, 3, nameof(kernelSizeX));
        ArgumentOutOfRangeException.ThrowIfLessThan(kernelSizeY, 3, nameof(kernelSizeY));

        if (kernelSizeX % 2 == 0 || kernelSizeY % 2 == 0)
        {
            throw new ArgumentException("Kernel size must be odd");
        }

        if (kernel.Length != kernelSizeX * kernelSizeY)
        {
            throw new ArgumentException("Kernel size must be equal to kernel size");
        }

        _material = material.CreateInstance();


        _kernelSizeX = kernelSizeX;
        _kernelSizeY = kernelSizeY;
        _kernelBuffer = renderingSystem.CreateGraphicsArrayBuffer<float>(kernelSizeX * kernelSizeY);
        SetKernel(kernel);

        _material.SetBuffer(ShaderResourceId.GaussianKernel, _kernelBuffer);
    }

    public void SetKernel(ReadOnlySpan<float> kernel)
    {
        if (kernel.Length != _kernelSizeX * _kernelSizeY)
        {
            throw new ArgumentException("Kernel size must be equal to kernel size");
        }

        _kernelSum = 0;
        for (int i = 0; i < _kernelBuffer.Length; i++)
        {
            _kernelBuffer[i] = kernel[i];
            _kernelSum += kernel[i];
        }

        _kernelBuffer.UpdateBuffer();
    }

    public void Compute(GPUCommandBuffer.ComputePass computePass, RenderTexture input, RenderTexture output)
    {
        ComputeRegion(computePass, input, output, 0, 0, (int)input.Width, (int)input.Height);
    }

    /// <summary>
    /// Runs the blur over a sub-rectangle only. Texels outside the region are not dispatched and
    /// keep their last computed values; threads past the region edge (dispatch groups round up to
    /// the thread-group size) return early so frozen texels are never rewritten.
    /// </summary>
    /// <param name="computePass">The compute pass to record the dispatch to.</param>
    /// <param name="input">The input texture; must match the output's size.</param>
    /// <param name="output">The output texture; must match the input's size.</param>
    /// <param name="x">The x origin of the region, in texels.</param>
    /// <param name="y">The y origin of the region, in texels.</param>
    /// <param name="width">The width of the region, in texels.</param>
    /// <param name="height">The height of the region, in texels.</param>
    public void ComputeRegion(GPUCommandBuffer.ComputePass computePass, RenderTexture input, RenderTexture output, int x, int y, int width, int height)
    {
        if (input.Width != output.Width || input.Height != output.Height)
        {
            throw new ArgumentException("Input and output must have the same size");
        }

        GaussianBlurConstant constant = new GaussianBlurConstant
        {
            texSize = new int2(input.Width, input.Height),
            kernelSize = new int2(_kernelSizeX, _kernelSizeY),
            rectOrigin = new int2(x, y),
            rectSize = new int2(width, height),
            kernelSum = _kernelSum,
        };

        _material.TrySetRenderTexture(ShaderResourceId.Input, input);
        _material.TrySetRenderTexture(ShaderResourceId.Output, output);

        _material.ReflectionInfo.Size.GetDispatchCount((uint)width, (uint)height, 1, out uint groupX, out uint groupY, out _);
        _material.DispatchByGroupWithConstant(computePass, groupX, groupY, 1, constant);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kernelBuffer.Dispose();
        }
    }
}

