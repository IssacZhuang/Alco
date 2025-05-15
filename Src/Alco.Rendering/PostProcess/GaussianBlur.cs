
using Alco.Graphics;

namespace Alco.Rendering;

public class GaussianBlur : AutoDisposable
{

    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _command;
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

        _device = renderingSystem.GraphicsDevice;
        _command = _device.CreateCommandBuffer();
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

    public void Blit(RenderTexture input, RenderTexture output)
    {
        if (input.Width != output.Width || input.Height != output.Height)
        {
            throw new ArgumentException("Input and output must have the same size");
        }

        GaussianBlurConstant constant = new GaussianBlurConstant
        {
            texSize = new int2(input.Width, input.Height),
            kernelSize = new int2(_kernelSizeX, _kernelSizeY),
            kernelSum = _kernelSum,
        };

        _material.TrySetRenderTexture(ShaderResourceId.Input, input);
        _material.TrySetRenderTexture(ShaderResourceId.Output, output);

        _command.Begin();
        _material.DispatchBySizeWithConstant(
            _command,
            input.Width,
            input.Height,
            1,
            constant
        );
        _command.End();
        _device.Submit(_command);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _kernelBuffer.Dispose();
            _command.Dispose();
        }
    }
}

