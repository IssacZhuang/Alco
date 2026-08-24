using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Provides BC3 (DXT5) texture compression functionality using compute shaders.
/// BC3 compression is commonly used for textures with alpha channels.
/// </summary>
public sealed class TextureCompressorBC3 : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandCompress;
    private readonly GPUCommandBuffer _commandCopy;

    private GraphicsArrayBuffer<uint4> _blocks;//resizeable

    // One compute material per sRGB state, created from the two injected
    // specializations of the texture-compress-bc3 module (MainCS<let IsSRGB>).
    private readonly ComputeMaterial _linearMaterial;
    private readonly ComputeMaterial _srgbMaterial;
    private ComputeMaterial _material;
    private bool _isSRGB;

    public bool IsSRGB
    {
        get => _isSRGB;
        set
        {
            if (_isSRGB != value)
            {
                _isSRGB = value;
                ApplyMaterial();
            }
        }
    }


    /// <summary>
    /// Initializes a new instance of the <see cref="TextureCompressorBC3"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance.</param>
    /// <param name="linearShader">The linear-output specialization of the compression module (MainCS&lt;let IsSRGB&gt; with IsSRGB = 0).</param>
    /// <param name="srgbShader">The sRGB-output specialization of the compression module (MainCS&lt;let IsSRGB&gt; with IsSRGB = 1).</param>
    /// <param name="defaultBufferSize">Initial capacity of the block staging buffer.</param>
    internal TextureCompressorBC3(RenderingSystem renderingSystem, Shader linearShader, Shader srgbShader,
        int defaultBufferSize = 256 * 256)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _commandCompress = _device.CreateCommandBuffer("texture_compressor_command_buffer");
        _commandCopy = _device.CreateCommandBuffer("texture_compressor_copy_command_buffer");

        _blocks = renderingSystem.CreateGraphicsArrayBuffer<uint4>(defaultBufferSize);
        _blocks.UpdateBuffer();

        _linearMaterial = renderingSystem.CreateComputeMaterial(linearShader);
        _linearMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);
        _srgbMaterial = renderingSystem.CreateComputeMaterial(srgbShader);
        _srgbMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);

        ApplyMaterial();
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

        if (source.Width % 4 != 0 || source.Height % 4 != 0)
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

        if (source.Width % 4 != 0 || source.Height % 4 != 0)
        {
            throw new InvalidOperationException("Texture width and height must be divisible by 4");
        }

        var texture = _renderingSystem.CreateTexture2D(source.Width, source.Height, source.Sampler, ImageLoadOption.Default with
        {
            Format = PixelFormat.BC3RGBAUnorm
        });



        CompressToTextureCore(source, texture);


        return texture;
    }

    private void CompressToTextureCore(Texture2D source, Texture2D target)
    {
        uint blocksX = source.Width / 4;
        uint blocksY = source.Height / 4;

        EnsureBufferSize(blocksX, blocksY);

        _material.SetTexture(ShaderResourceId.Input, source);

        _commandCompress.Begin();

        using (var computePass = _commandCompress.BeginCompute())
        {
            _material.DispatchBySizeWithConstant(computePass, blocksX, blocksY, 1, new uint2(blocksX, blocksY));
        }

        _commandCompress.End();
        _device.Submit(_commandCompress);


        _commandCopy.Begin();
        _commandCopy.CopyBufferToTexture(_blocks.NativeBuffer, target.NativeTexture, 0, 0, TextureAspect.All);
        _commandCopy.End();
        _device.Submit(_commandCopy);

    }

    private void EnsureBufferSize(uint blocksX, uint blocksY)

    {
        uint requiredSize = blocksX * blocksY;
        if (_blocks.Length < requiredSize)
        {
            uint newSize = _blocks.Size * 2;

            _blocks.Dispose();
            while (newSize < requiredSize)
            {
                newSize *= 2;
            }

            _blocks = _renderingSystem.CreateGraphicsArrayBuffer<uint4>((int)newSize);
            // The staging buffer was replaced: rebind it on both materials.
            _linearMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);
            _srgbMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);
        }
    }

    /// <summary>
    /// Switches to the compute material of the current sRGB state. Both the
    /// linear and sRGB materials arrive injected from the caller (they are the
    /// two generic value specializations of the compression module,
    /// MainCS&lt;let IsSRGB&gt;): 1 selects the sRGB output path, 0 the linear one.
    /// </summary>
    private void ApplyMaterial()
    {
        _material = _isSRGB ? _srgbMaterial : _linearMaterial;
    }

    /// <summary>
    /// Releases the unmanaged resources used by the TextureCompressorBC3 and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blocks.Dispose();
            _commandCompress.Dispose();
        }
    }
}
