using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

/// <summary>
/// A GPUTexture with a TextureView which the dimension is 2D
/// </summary>
public class Texture2D : ShaderResource
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

    public override string Name { get; }

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

    internal Texture2D(
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


}