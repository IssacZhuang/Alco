using Alco.Graphics;
using StbImageSharp;
using static Alco.Unsafe.UtilsMemory;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

public class Texture2D : Texture
{
    // bind group include texture and sampeler
    private GPUResourceGroup? _resourcesSample;

    // bind gorup only include texture
    private GPUResourceGroup? _resourcesRead;
    private GPUResourceGroup? _resourcesStorage;

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

    internal Texture2D(GPUDevice device, GPUTexture texture, GPUTextureView textureView, GPUSampler sampler) : base(device, texture, textureView, sampler)
    {

    }

    public unsafe void SetPixels<T>(Bitmap<T> bitmap) where T : unmanaged
    {
        if (!IsWriteable)
        {
            throw new InvalidOperationException("The texture is not writeable");
        }


        if (bitmap.Width != Width || bitmap.Height != Height)
        {
            throw new ArgumentException("The size of the bitmap does not match the size of the texture");
        }

        _device.WriteTexture(_texture, bitmap);;
    }

    public void UnsafeHotReload(GPUTexture texture, GPUTextureView textureView)
    {
        _texture = texture;
        _textureView = textureView;

        //just let them collect by GC
        _resourcesSample = null;
        _resourcesRead = null;
        _resourcesStorage = null;
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
            _device.BindGroupTexture2DStorage,
            new ResourceBindingEntry[]{
                new ResourceBindingEntry(0, _textureView),
            }
        );

        return _device.CreateResourceGroup(descriptor);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            //dispose non-private managed resources
            _resourcesSample?.Dispose();
            _resourcesRead?.Dispose();
            _resourcesStorage?.Dispose();
        }

    }
}