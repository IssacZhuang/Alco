using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Provides BC3 (DXT5) texture compression functionality using compute shaders.
/// BC3 compression is commonly used for textures with alpha channels.
/// </summary>
public class TextureCompressorBC3 : AutoDisposable
{
    private readonly ComputeMaterial _material;
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandBuffer;
    private readonly GraphicsValueBuffer<uint4> _data;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureCompressorBC3"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance.</param>
    /// <param name="material">The compute material containing the BC3 compression shader.</param>
    internal TextureCompressorBC3(RenderingSystem renderingSystem, ComputeMaterial material)
    {
        _renderingSystem = renderingSystem;
        _material = material.CreateInstance();
        _device = renderingSystem.GraphicsDevice;
        _commandBuffer = _device.CreateCommandBuffer("texture_compressor_command_buffer");
        _data = renderingSystem.CreateGraphicsValueBuffer<uint4>("texture_compressor_data");
        _material.TrySetBuffer(ShaderResourceId.Data, _data);
    }

    /// <summary>
    /// Attempts to compress the source texture using BC3 compression.
    /// </summary>
    /// <param name="source">The source texture to compress.</param>
    /// <param name="texture">When this method returns, contains the compressed texture if compression was successful, or null if compression failed.</param>
    /// <returns>true if BC3 compression is supported by GPU and compression was successful; otherwise, false.</returns>
    /// <remarks>
    /// This method checks for BC3 compression support before attempting compression.
    /// If BC3 compression is not supported by the device, the method returns false.
    /// </remarks>
    public bool TryCompress(Texture2D source, [NotNullWhen(true)] out Texture2D? texture)
    {
        if (!_device.TextureCompressBC3Supported)
        {
            texture = null;
            return false;
        }

        texture = Compress(source);
        return true;
    }

    /// <summary>
    /// Compresses the source texture using BC3 compression.
    /// </summary>
    /// <param name="source">The source texture to compress.</param>
    /// <returns>A new texture containing the BC3 compressed data.</returns>
    /// <exception cref="InvalidOperationException">Thrown when BC3 compression is not supported by the device.</exception>
    /// <remarks>
    /// This method performs BC3 compression using a compute shader.
    /// The resulting texture will have the same dimensions as the source but will use the BC3RGBAUnorm format.
    /// An exception will be thrown if BC3 compression is not supported by the device. Use <see cref="GPUDevice.TextureCompressBC3Supported"/> to check for support
    /// or use <see cref="TryCompress"/> method to avoid exceptions.
    /// </remarks>
    public Texture2D Compress(Texture2D source)
    {
        if (!_device.TextureCompressBC3Supported)
        {
            throw new InvalidOperationException("Texture compression BC3 is not supported");
        }

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

    /// <summary>
    /// Releases the unmanaged resources used by the TextureCompressorBC3 and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commandBuffer.Dispose();
            _data.Dispose();
        }
    }
}
