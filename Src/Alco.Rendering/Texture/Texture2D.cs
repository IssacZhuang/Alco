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

    // bind group include texture and sampaler
    private GPUResourceGroup? _resourcesSample;

    // bind gorup only include texture
    private GPUResourceGroup? _resourcesRead;

    private GPUBindGroup? _bindGroupStorage;
    private readonly GPUResourceGroup?[] _resourcesStorage;
    private readonly GPUResourceGroup?[] _resourcesReadMip;
    private readonly GPUTextureView?[] _mipViews;

    /// <summary>
    /// The number of mip levels of the texture.
    /// </summary>
    public uint MipLevels { get; }


    public override GPUResourceGroup EntrySample
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

    public override GPUResourceGroup EntryReadonly
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

    public override GPUResourceGroup EntryWriteable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return EntryStorage(0);
        }
    }

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

        MipLevels = texture.MipLevelCount;
        _resourcesStorage = new GPUResourceGroup?[MipLevels];
        _resourcesReadMip = new GPUResourceGroup?[MipLevels];
        _mipViews = new GPUTextureView?[MipLevels];
        _defaultSprite = new Sprite("default", this, Rect.One);
    }

    /// <summary>
    /// The read-only resource group bound to the single-mip view of the given mip level.
    /// Inside the view the mip is rebased to mip 0, so shaders load it with mip index 0.
    /// <br/>Use this instead of <see cref="EntryReadonly"/> when the same dispatch also writes
    /// another mip of the texture: the non-overlapping subresource ranges of the two views
    /// avoid the usage scope conflict of the underlying graphics API.
    /// </summary>
    /// <param name="mipLevel">The mip level to read (0 = full resolution).</param>
    /// <returns>The read-only resource group for the mip level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mip level is out of range.</exception>
    public GPUResourceGroup EntryReadonlyMip(uint mipLevel)
    {
        CheckMipLevel(mipLevel);

        if (_resourcesReadMip[mipLevel] == null)
        {
            ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
                _device.BindGroupTexture2DRead,
                new ResourceBindingEntry[]{
                    new ResourceBindingEntry(0, GetMipView(mipLevel)),
                }
            );

            _resourcesReadMip[mipLevel] = _device.CreateResourceGroup(descriptor);
        }

        return _resourcesReadMip[mipLevel]!;
    }

    /// <summary>
    /// The storage resource group bound to the single-mip view of the given mip
    /// level, for compute passes that write that mip.
    /// </summary>
    /// <param name="mipLevel">The mip level to write (0 = full resolution).</param>
    /// <returns>The storage resource group for the mip level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mip level is out of range.</exception>
    public GPUResourceGroup EntryStorage(uint mipLevel)
    {
        CheckMipLevel(mipLevel);

        if (_bindGroupStorage == null)
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
                        new StorageTextureBindingInfo(AccessMode.ReadWrite, TextureViewDimension.Texture2D, _texture.PixelFormat)),
                }
            });
        }

        if (_resourcesStorage[mipLevel] == null)
        {
            ResourceGroupDescriptor descriptor = new ResourceGroupDescriptor(
                _bindGroupStorage,
                new ResourceBindingEntry[]{
                    new ResourceBindingEntry(0, GetMipView(mipLevel)),
                }
            );

            _resourcesStorage[mipLevel] = _device.CreateResourceGroup(descriptor);
        }

        return _resourcesStorage[mipLevel]!;
    }

    internal GPUTextureView GetMipView(uint mipLevel)
    {
        if (_mipViews[mipLevel] == null)
        {
            _mipViews[mipLevel] = _device.CreateTextureView(new TextureViewDescriptor(
                _texture,
                TextureViewDimension.Texture2D,
                mipLevel,
                1,
                name: $"{Name}_mip_view_{mipLevel}"));
        }

        return _mipViews[mipLevel]!;
    }

    private void CheckMipLevel(uint mipLevel)
    {
        if (mipLevel >= MipLevels)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), mipLevel, "The mip level is out of range.");
        }
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

    /// <summary>
    /// Implicitly converts a Texture2D to its default Sprite.
    /// </summary>
    /// <param name="texture">The texture to convert.</param>
    /// <returns>The default sprite of the texture.</returns>
    public static implicit operator Sprite(Texture2D texture)
    {
        return texture._defaultSprite;
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
        _bindGroupStorage = null;
        for (int i = 0; i < _resourcesStorage.Length; i++)
        {
            _resourcesStorage[i] = null;
            _resourcesReadMip[i] = null;
            _mipViews[i] = null;
        }
    }

    public override void SetSampler(GPUSampler sampler)
    {
        base.SetSampler(sampler);
        _resourcesSample = null;
        _resourcesRead = null;
        _bindGroupStorage = null;
        for (int i = 0; i < _resourcesStorage.Length; i++)
        {
            _resourcesStorage[i] = null;
            _resourcesReadMip[i] = null;
        }
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

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            //dispose non-private managed resources
            _resourcesSample?.Dispose();
            _resourcesRead?.Dispose();
            _bindGroupStorage?.Dispose();
            for (int i = 0; i < _resourcesStorage.Length; i++)
            {
                _resourcesStorage[i]?.Dispose();
                _resourcesReadMip[i]?.Dispose();
                _mipViews[i]?.Dispose();
            }
        }

    }
}
