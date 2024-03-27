using System.Runtime.CompilerServices;
using Vocore.Graphics;



namespace Vocore.Rendering;

/// <summary>
/// High level encapsulation of a GPUTexture with a TextureView which the dimension is 2D
/// </summary>
public abstract class Texture : ShaderResource
{
    // bind group include texture and sampeler
    private GPUResourceGroup? _resourcesSample;

    // bind gorup only include texture
    private GPUResourceGroup? _resourcesRead;
    private GPUResourceGroup? _resourcesStorage;

    private readonly GPUDevice _device;
    // internal
    private readonly GPUTexture _texture;
    private readonly GPUTextureView _textureView;

    // from outside
    private readonly GPUSampler _sampler;

    public string Name { get; }

    public abstract bool IsReadOnly { get; }

    public GPUResourceGroup EntrySample
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_resourcesSample == null)
            {
                _resourcesSample = CreateResourcesSample();
            }

            return _resourcesSample;
        }
    }

    public GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_resourcesRead == null)
            {
                _resourcesRead = CreateResourceGroupRead();
            }

            return _resourcesRead;
        }
    }

    public GPUResourceGroup EntryWriteable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_resourcesStorage == null)
            {
                _resourcesStorage = CreateResourceGroupStorage();
            }

            return _resourcesStorage;
        }
    }


    public uint Width
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Width;
    }

    public uint Hieght
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture.Height;
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
        _texture.Dispose();
        _textureView.Dispose();
        //the sampler is not disposed here because it is a shared resource

        _resourcesSample?.Dispose();
        _resourcesRead?.Dispose();
        _resourcesStorage?.Dispose();
    }

    private GPUResourceGroup CreateResourcesSample()
    {
        ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DSampled,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _textureView),
                new ResourceBindingEntry(1, _sampler)
            }
        );

        return _device.CreateResourceGroup(descriptor);
    }

    private GPUResourceGroup CreateResourceGroupRead()
    {
        ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
            _device.BindGroupTexture2DRead,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _textureView),
            }
        );

        return _device.CreateResourceGroup(descriptor);
    }

    private GPUResourceGroup CreateResourceGroupStorage()
    {
        ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
            _device.BindGroupStorageTexture2D,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _textureView),
            }
        );

        return _device.CreateResourceGroup(descriptor);
    }


    #region Texture Creation

    #endregion


}