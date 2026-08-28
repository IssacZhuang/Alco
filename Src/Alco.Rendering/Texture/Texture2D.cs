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

    // bind group only include texture
    private GPUResourceGroup? _resourcesRead;

    private GPUBindGroup? _bindGroupStorage;
    private readonly GPUResourceGroup?[] _resourcesStorage;
    private readonly GPUResourceGroup?[] _resourcesReadMip;
    private readonly GPUTextureView?[] _mipViews;

    private volatile Task? _contentUpload;
    private volatile int _contentPresent = 1;

    /// <summary>
    /// The number of mip levels of the texture.
    /// </summary>
    public uint MipLevels { get; }


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

    /// <summary>
    /// A task that completes when streamed content has been issued to the GPU queue,
    /// or is already completed for textures created with their content. It completes
    /// even when the upload failed — check <see cref="IsContentLoaded"/> to distinguish —
    /// and never faults. Completion covers the CPU-side queue write only: backends
    /// order queue operations, so work submitted afterwards observes the content.
    /// </summary>
    public Task ContentArrival => _contentUpload ?? Task.CompletedTask;

    /// <summary>
    /// Whether the texture's content is present on the GPU. False only between a
    /// streaming load's creation and its successful in-place upload; sampling the
    /// texture meanwhile yields transparent black.
    /// </summary>
    public bool IsContentLoaded => _contentPresent != 0;

    /// <summary>
    /// Marks the texture as awaiting streamed content: <see cref="IsContentLoaded"/>
    /// stays false until <see cref="MarkContentLoaded"/> follows a successful upload.
    /// </summary>
    internal void MarkContentPending()
    {
        _contentPresent = 0;
    }

    /// <summary>
    /// Attaches the streaming upload task exposed through <see cref="ContentArrival"/>.
    /// </summary>
    /// <param name="upload">The upload task; it must never fault.</param>
    internal void SetContentUpload(Task upload)
    {
        _contentUpload = upload;
    }

    /// <summary>
    /// Marks the texture's content as present after a successful full upload.
    /// </summary>
    internal void MarkContentLoaded()
    {
        _contentPresent = 1;
    }

    internal Texture2D(
        GPUDevice device,
        GPUTexture texture,
        GPUTextureView textureView,
        Padding? slicePadding = null,
        bool ownsResources = true
        ) :
        base(device, texture, textureView, ownsResources)
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

        _device.WriteTexture(_texture, bitmap);
        MarkContentLoaded();
    }

    public void UnsafeHotReload(GPUTexture texture, GPUTextureView textureView)
    {
        _texture = texture;
        _textureView = textureView;

        //just let them collect by GC
        _resourcesRead = null;
        _bindGroupStorage = null;
        for (int i = 0; i < _resourcesStorage.Length; i++)
        {
            _resourcesStorage[i] = null;
            _resourcesReadMip[i] = null;
            _mipViews[i] = null;
        }

        DiscardLayoutResourceGroups();
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
