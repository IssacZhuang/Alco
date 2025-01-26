using System.Runtime.CompilerServices;
using Alco.Graphics;



namespace Alco.Rendering;

/// <summary>
/// High level encapsulation of a GPUTexture with a TextureView which the dimension is 2D
/// </summary>
public abstract class Texture : AutoDisposable
{
    protected readonly GPUDevice _device;
    // internal
    protected GPUTexture _texture;
    protected GPUTextureView _textureView;

    // from outside
    protected readonly GPUSampler _sampler;

    public string Name { get; }

    public abstract bool IsReadOnly { get; }

    


    public uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Width;
    }

    public uint Height
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Height;
    }

    public uint Depth
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Depth;
    }

    internal Texture(
        GPUDevice device,
        GPUTexture texture,
        GPUTextureView textureView,
        GPUSampler sampler)
    {
        _device = device;

        _texture = texture;
        _textureView = textureView;
        _sampler = sampler;

        Name = texture.Name;
    }

    public unsafe void SetPixels(Color32[] data)
    {
        fixed (Color32* ptr = data)
        {
            SetPixels(ptr, (uint)data.Length);
        }
    }

    public unsafe void SetPixels(Color32* data, uint length)
    {

        if (IsReadOnly)
        {
            throw new InvalidOperationException("Can not set pixels to a readonly texture");
        }

        if (length != _texture.Width * _texture.Height)
        {
            throw new ArgumentException($"The pxiel count {length} is not equal to the texture size(width*height)");
        }

        _device.WriteTexture(_texture, (byte*)data, length, 4);
    }

    public unsafe void SetPixels(byte* data, uint size, uint pixelSize)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("Can not set pixels to a readonly texture");
        }
        _device.WriteTexture(_texture, data, size, pixelSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            //dispose non-private managed resources
            _texture?.Dispose();
            _textureView?.Dispose();
        }
    }


    #region Texture Creation

    #endregion


}