using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Provides BC1 (DXT1) texture compression functionality using compute shaders.
/// BC1 stores opaque RGB at 4 bits per pixel; use <see cref="TextureCompressorBC3"/>
/// for textures with an alpha channel.
/// </summary>
public sealed class TextureCompressorBC1 : AutoDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly GPUDevice _device;
    private readonly GPUCommandBuffer _commandCompress;

    private GraphicsArrayBuffer<uint2> _blocks; // resizeable, one uint2 per 4x4 block

    // Linear/sRGB are construction-time specializations of MainCS<let IsSRGB>.
    private readonly Shader _shader;
    private ComputeMaterial _linearMaterial = null!;
    private ComputeMaterial _srgbMaterial = null!;
    private ComputeMaterial _material = null!;
    private bool _isSRGB;

    /// <summary>Whether the compressed blocks are encoded for sRGB sampling.</summary>
    public bool IsSRGB
    {
        get => _isSRGB;
        set
        {
            if (_isSRGB != value)
            {
                _isSRGB = value;
                _material = _isSRGB ? _srgbMaterial : _linearMaterial;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureCompressorBC1"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system instance.</param>
    /// <param name="shader">The texture-compress-bc1 shader (MainCS&lt;let IsSRGB&gt;;
    /// the linear/sRGB dispatchers are its specializations).</param>
    /// <param name="defaultBufferSize">Initial capacity of the block staging buffer.</param>
    internal TextureCompressorBC1(RenderingSystem renderingSystem, Shader shader,
        int defaultBufferSize = 256 * 256)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        _shader = shader;
        _commandCompress = _device.CreateCommandBuffer("texture_compressor_bc1_command_buffer");

        _blocks = renderingSystem.CreateGraphicsArrayBuffer<uint2>(defaultBufferSize);
        _blocks.UpdateBuffer();

        _linearMaterial = renderingSystem.CreateComputeMaterial(shader, false);
        _linearMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);
        _srgbMaterial = renderingSystem.CreateComputeMaterial(shader, true);
        _srgbMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);

        _material = _linearMaterial;
    }

    /// <summary>
    /// Compress the source texture on the GPU and read the BC1 blocks back to CPU
    /// memory, without creating an intermediate block-compressed GPU texture.
    /// </summary>
    /// <param name="source">The source texture; both dimensions must be multiples of 4.</param>
    /// <param name="destination">The destination span; must hold at least
    /// <c>blocksX * blocksY * 8</c> bytes.</param>
    /// <returns>The number of block bytes written (one uint2 per 4x4 block, row-major).</returns>
    /// <exception cref="InvalidOperationException">BC compression is not supported by
    /// the device, or the source dimensions are not multiples of 4.</exception>
    /// <exception cref="ArgumentException">The destination span is too small.</exception>
    public unsafe int CompressBlocks(Texture2D source, Span<byte> destination)
    {
        if (!_device.IsFeatureSupported(GPUFeatures.TextureCompressionBC))
        {
            throw new InvalidOperationException("Texture compression BC1 is not supported");
        }

        if (source.Width % 4 != 0 || source.Height % 4 != 0)
        {
            throw new InvalidOperationException("Texture width and height must be divisible by 4");
        }

        uint blocksX = source.Width / 4;
        uint blocksY = source.Height / 4;
        int byteCount = (int)(blocksX * blocksY * (uint)sizeof(uint2));

        EnsureBufferSize(blocksX, blocksY);

        _material.SetTexture(ShaderResourceId.Input, source);

        _commandCompress.Begin();
        using (var computePass = _commandCompress.BeginCompute())
        {
            _material.DispatchBySizeWithConstant(computePass, blocksX, blocksY, 1, new uint2(blocksX, blocksY));
        }
        _commandCompress.End();
        _device.Submit(_commandCompress);

        if (destination.Length < byteCount)
        {
            throw new ArgumentException($"The destination span holds {destination.Length} bytes but the compressed blocks need {byteCount}.");
        }

        fixed (byte* dest = destination)
        {
            _device.ReadBuffer(_blocks.NativeBuffer, dest, 0, (uint)byteCount);
        }
        return byteCount;
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

            _blocks = _renderingSystem.CreateGraphicsArrayBuffer<uint2>((int)newSize);
            // The staging buffer was replaced: rebind it on both dispatchers.
            _linearMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);
            _srgbMaterial.TrySetBuffer(ShaderResourceId.Output, _blocks);
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the TextureCompressorBC1 and optionally releases the managed resources.
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
