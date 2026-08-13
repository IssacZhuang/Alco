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
    protected GPUSampler _sampler;

    // Whether this wrapper owns _texture and _textureView. Wrappers created over
    // externally owned GPU resources (e.g. frame buffer attachments or render
    // graph pooled textures) are non-owning: their lifetime is managed by the
    // creator, the same rule as the externally supplied sampler.
    private readonly bool _ownsResources;

    public string Name { get; }

    public bool IsWriteable => _texture.IsWriteable;


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

    public GPUTexture NativeTexture => _texture;

    /// <summary>
    /// The full-chain texture view of the texture.
    /// </summary>
    public GPUTextureView View
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _textureView;
    }

    /// <summary>
    /// The sampler used when the texture is bound to a texture-and-sampler slot.
    /// </summary>
    public GPUSampler Sampler
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _sampler;
    }

    /// <summary>
    /// The resource group containing the texture view and the sampler, for
    /// texture-and-sampler shader bind groups.
    /// </summary>
    public abstract GPUResourceGroup EntrySample { get; }

    /// <summary>
    /// The resource group containing only the texture view, for read-only texture
    /// shader bind groups.
    /// </summary>
    public abstract GPUResourceGroup EntryReadonly { get; }

    /// <summary>
    /// The resource group containing only the texture view, for storage texture
    /// shader bind groups.
    /// </summary>
    public abstract GPUResourceGroup EntryWriteable { get; }

    internal Texture(
        GPUDevice device,
        GPUTexture texture,
        GPUTextureView textureView,
        GPUSampler sampler,
        bool ownsResources = true)
    {
        _device = device;

        _texture = texture;
        _textureView = textureView;
        _sampler = sampler;
        _ownsResources = ownsResources;

        Name = texture.Name;
    }

    public unsafe void SetPixels<T>(T[] data) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            SetPixels(ptr, (uint)data.Length);
        }
    }

    public unsafe void SetPixels<T>(T* data, uint length) where T : unmanaged
    {
        if (!IsWriteable)
        {
            throw new InvalidOperationException("Can not set pixels to a readonly texture");

        }

        if (length != _texture.Width * _texture.Height)
        {
            throw new ArgumentException($"The pxiel count {length} is not equal to the texture size(width*height)");
        }

        _device.WriteTexture(_texture, (byte*)data, length * (uint)sizeof(T));
    }

    public unsafe void SetPixels(byte* data, uint size)
    {
        if (!IsWriteable)
        {
            throw new InvalidOperationException("Can not set pixels to a readonly texture");
        }
        _device.WriteTexture(_texture, data, size);
    }

    public virtual void SetSampler(GPUSampler sampler)
    {
        _sampler = sampler;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsResources)
        {
            //dispose non-private managed resources
            _texture?.Dispose();
            _textureView?.Dispose();
        }
    }


    #region Texture Creation

    #endregion


}