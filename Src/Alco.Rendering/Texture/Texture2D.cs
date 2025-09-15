using Alco.Graphics;
using System.Runtime.CompilerServices;
using System.Numerics;
using Alco;
using System.Diagnostics.CodeAnalysis;

namespace Alco.Rendering;

public sealed class Texture2D : Texture
{
    private readonly Sprite _defaultSprite;
    private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

    // bind group include texture and sampeler
    private GPUResourceGroup? _resourcesSample;

    // bind gorup only include texture
    private GPUResourceGroup? _resourcesRead;

    private GPUBindGroup? _bindGroupStorage;
    private GPUResourceGroup? _resourcesStorage;//todo: make it shared


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

    public GPUSampler Sampler => _sampler;

    public Padding SlicePadding { get; }

    internal Texture2D(
        GPUDevice device,
        GPUTexture texture,
        GPUTextureView textureView,
        GPUSampler sampler,
        Padding? slicePadding = null
        ) :
        base(device, texture, textureView, sampler)
    {
        if (slicePadding.HasValue)
        {
            SlicePadding = slicePadding.Value;
        }
        else
        {
            SlicePadding = Padding.Zero;
        }

        _defaultSprite = new Sprite("default", this, Rect.One);
    }

    public void ClearSprites()
    {
        _sprites.Clear();
    }

    public void SetSprite(string name, Rect uvRect)
    {
        _sprites[name] = new Sprite(name, this, uvRect);
    }

    public bool TryGetSprite(string name, [NotNullWhen(true)] out Sprite? sprite)
    {
        return _sprites.TryGetValue(name, out sprite);
    }

    public Sprite GetSprite(string name)
    {
        if (_sprites.TryGetValue(name, out Sprite? sprite))
        {
            return sprite;
        }
        return _defaultSprite;
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

        _device.WriteTexture(_texture, bitmap); ;
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

    public override void SetSampler(GPUSampler sampler)
    {
        base.SetSampler(sampler);
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
        _bindGroupStorage = _device.CreateBindGroup(new BindGroupDescriptor
        {
            Name = $"{Name}_bind_group_storage_texture",
            Bindings = new BindGroupEntry[]{
                    new BindGroupEntry(
                        0,
                        ShaderStage.Standard,
                        BindingType.StorageTexture,
                        null,
                        new StorageTextureBindingInfo(AccessMode.ReadWrite, TextureViewDimension.Texture2D,_texture.PixelFormat)),
                }
        });

        ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
            _bindGroupStorage,
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
            _bindGroupStorage?.Dispose();
            _resourcesStorage?.Dispose();
        }

    }
}