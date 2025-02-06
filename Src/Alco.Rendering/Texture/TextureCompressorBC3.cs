using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;

namespace Alco.Rendering;

public class TextureCompressorBC3 : AutoDisposable
{
    private readonly ComputeMaterial _material;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly GraphicsValueBuffer<uint4> _data;
    internal TextureCompressorBC3(RenderingSystem renderingSystem, ComputeMaterial material)
    {
        _renderingSystem = renderingSystem;
        _material = material.CreateInstance();
        _device = renderingSystem.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer("texture_compressor_command_buffer");
        _data = renderingSystem.CreateGraphicsValueBuffer<uint4>("texture_compressor_data");
        _material.TrySetBuffer(ShaderResourceId.Data, _data);
    }

    public Texture2D Compress(Texture2D source)
    {
        var texture = _renderingSystem.CreateTexture2D(source.Width, source.Height, source.Sampler, ImageLoadOption.Default with
        {
            Format = PixelFormat.BC3RGBAUnorm

        });

        _material.SetTexture(ShaderResourceId.Input, source);
        _material.SetTexture(ShaderResourceId.Output, texture);

        _data.UpdateBuffer(new uint4(0, 0, source.Width, source.Height));

        _commandBuffer.Begin();
        _material.DispatchBySize(_commandBuffer, source.Width, source.Height, 1);
        _commandBuffer.End();
        _device.Submit(_commandBuffer);


        return texture;
    }


    protected override void Dispose(bool disposing)

    {

    }
}
