using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A 2D GPU texture wrapping <see cref="Texture"/> with optional named sprites, 9-slice padding,
/// streaming arrival state, and hot-reload replacement.
/// </summary>
public sealed class Texture2D : AutoDisposable
{
    private readonly RenderingSystem _system;
    private Texture _texture;
    private bool _ownsResource;
    private readonly Sprite _defaultSprite;
    private readonly Dictionary<string, Sprite> _sprites = new();

    private volatile Task? _contentUpload;
    private volatile int _contentPresent = 1;

    internal readonly Lock UploadLock = new();

    /// <summary>The owning graphics rendering system.</summary>
    public RenderingSystem RenderingSystem => _system;

    /// <summary>The underlying GPU texture handle in <see cref="Alco.Graphics"/>.</summary>
    public Texture NativeTexture => _texture;

    /// <summary>The diagnostic name of this texture.</summary>
    public string Name { get; private set; }

    /// <summary>The texture width in texels.</summary>
    public uint Width { get; private set; }

    /// <summary>The texture height in texels.</summary>
    public uint Height { get; private set; }

    /// <summary>The pixel format of the underlying texture.</summary>
    public Format Format { get; private set; }

    /// <summary>The number of mip levels of the texture.</summary>
    public uint MipLevels { get; private set; }

    /// <summary>Whether the texture can receive CPU pixel uploads via transfer destination usage.</summary>
    public bool IsWriteable { get; private set; }

    /// <summary>Bindless sampled descriptor slot index for shaders.</summary>
    public uint SampledIndex => _texture.SampledIndex;

    /// <summary>Bindless storage descriptor slot index for mip 0.</summary>
    public uint StorageIndex => _texture.StorageIndex;

    /// <summary>The 9-slice padding, in texels, applied to sprites of this texture.</summary>
    public Padding SlicePadding { get; }

    /// <summary>
    /// A task that completes when streamed content has been issued to the GPU queue.
    /// It never faults; check <see cref="IsContentLoaded"/> to determine success.
    /// </summary>
    public Task ContentArrival => _contentUpload ?? Task.CompletedTask;

    /// <summary>
    /// Whether the texture's content is present on the GPU. False only between a
    /// streaming load's creation and its successful in-place upload.
    /// </summary>
    public bool IsContentLoaded => _contentPresent != 0;

    internal Texture2D(
        RenderingSystem system,
        Texture texture,
        string name = "unnamed_texture",
        Padding? slicePadding = null,
        bool ownsResources = true)
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(texture);

        _system = system;
        _texture = texture;
        _ownsResource = ownsResources;
        Name = name;
        SlicePadding = slicePadding ?? Padding.Zero;
        CacheTextureMetadata(texture);
        _defaultSprite = new Sprite("default", this, Rect.One);
    }

    /// <summary>Gets the bindless storage descriptor slot index for a specific mip level.</summary>
    /// <param name="mipLevel">The mip level (0 = full resolution).</param>
    /// <returns>The storage descriptor index.</returns>
    public uint GetStorageIndex(uint mipLevel) => _texture.GetStorageIndex(mipLevel);

    internal void MarkContentPending()
    {
        _contentPresent = 0;
    }

    internal void SetContentUpload(Task upload)
    {
        _contentUpload = upload;
    }

    internal void MarkContentLoaded()
    {
        _contentPresent = 1;
    }

    /// <summary>Removes all named sprites.</summary>
    public void ClearSprites()
    {
        _sprites.Clear();
    }

    /// <summary>Adds or replaces a named sprite over the given UV rectangle.</summary>
    /// <param name="name">The sprite name.</param>
    /// <param name="uvRect">The UV rectangle of the sprite within the texture.</param>
    public void SetSprite(string name, Rect uvRect)
    {
        _sprites[name] = new Sprite(name, this, uvRect);
    }

    /// <summary>Gets the sprite with the given name, returning false when it does not exist.</summary>
    /// <param name="name">The sprite name.</param>
    /// <param name="sprite">The found sprite, or null.</param>
    /// <returns>True when a sprite with the name exists.</returns>
    public bool TryGetSprite(string name, [NotNullWhen(true)] out Sprite? sprite)
    {
        return _sprites.TryGetValue(name, out sprite);
    }

    /// <summary>Gets the sprite with the given name, falling back to the default sprite when absent.</summary>
    /// <param name="name">The sprite name.</param>
    /// <returns>The named sprite, or the default sprite.</returns>
    public Sprite GetSprite(string name)
    {
        if (_sprites.TryGetValue(name, out Sprite? sprite))
        {
            return sprite;
        }
        return _defaultSprite;
    }

    /// <summary>Implicitly converts a <see cref="Texture2D"/> to its default <see cref="Sprite"/>.</summary>
    /// <param name="texture">The texture to convert.</param>
    /// <returns>The default sprite of the texture.</returns>
    public static implicit operator Sprite(Texture2D texture) => texture._defaultSprite;

    /// <summary>Implicitly converts a <see cref="Texture2D"/> to its underlying <see cref="Texture"/>.</summary>
    /// <param name="texture">The texture wrapper.</param>
    /// <returns>The underlying GPU texture.</returns>
    public static implicit operator Texture(Texture2D texture) => texture._texture;

    /// <summary>Uploads a CPU bitmap to the texture, replacing the whole content.</summary>
    /// <typeparam name="T">The pixel type; must match the texture's format.</typeparam>
    /// <param name="bitmap">The CPU bitmap to upload.</param>
    public unsafe void SetPixels<T>(Bitmap<T> bitmap) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (!IsWriteable)
        {
            throw new InvalidOperationException("The texture is not writeable.");
        }
        if (bitmap.Width != Width || bitmap.Height != Height)
        {
            throw new ArgumentException("The size of the bitmap does not match the size of the texture.");
        }

        Texture2DUtility.UploadTextureData(
            _system,
            _texture,
            new ReadOnlySpan<byte>(bitmap.UnsafePointer, checked(bitmap.Width * bitmap.Height * sizeof(T))));
        MarkContentLoaded();
    }

    /// <summary>Uploads a sub-rectangle of a CPU bitmap into this texture, leaving outside texels untouched.</summary>
    /// <typeparam name="T">The pixel type; must match the texture's format.</typeparam>
    /// <param name="bitmap">The CPU bitmap holding the region's source data.</param>
    /// <param name="x">The x origin of the region, in texels.</param>
    /// <param name="y">The y origin of the region, in texels.</param>
    /// <param name="width">The width of the region, in texels.</param>
    /// <param name="height">The height of the region, in texels.</param>
    public unsafe void SetPixels<T>(Bitmap<T> bitmap, int x, int y, int width, int height) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (!IsWriteable)
        {
            throw new InvalidOperationException("The texture is not writeable.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (x + width > Width || y + height > Height || x + width > bitmap.Width || y + height > bitmap.Height)
        {
            throw new ArgumentOutOfRangeException(
                $"The region ({x}, {y}, {width}, {height}) exceeds the texture size ({Width}, {Height}) or bitmap size ({bitmap.Width}, {bitmap.Height}).");
        }
        if (width == 0 || height == 0)
        {
            return;
        }

        int pixelSize = sizeof(T);
        long srcRowBytes = (long)bitmap.Width * pixelSize;
        byte* start = (byte*)bitmap.UnsafePointer + (long)y * srcRowBytes + (long)x * pixelSize;
        int regionSliceLength = checked((int)((long)(height - 1) * srcRowBytes + (long)width * pixelSize));

        Texture2DUtility.UploadTextureData(
            _system,
            _texture,
            new ReadOnlySpan<byte>(start, regionSliceLength),
            new TextureCopyDesc
            {
                Offset = new Extent3D((uint)x, (uint)y, 0),
                Extent = new Extent3D((uint)width, (uint)height, 1),
                RowPitchBytes = (ulong)srcRowBytes,
            });
        MarkContentLoaded();
    }

    /// <summary>Replaces the underlying GPU texture in place for hot reloading.</summary>
    /// <param name="texture">The new GPU texture.</param>
    /// <param name="name">Optional replacement name; null keeps the existing name.</param>
    /// <param name="ownsResources">Whether this wrapper owns and destroys the new texture.</param>
    public void UnsafeHotReload(Texture texture, string? name = null, bool ownsResources = true)
    {
        ArgumentNullException.ThrowIfNull(texture);

        lock (UploadLock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_ownsResource && !_texture.IsDestroyed && !ReferenceEquals(_texture, texture))
            {
                _texture.Destroy();
            }

            _texture = texture;
            _ownsResource = ownsResources;
            if (name != null)
            {
                Name = name;
            }
            CacheTextureMetadata(texture);
        }
    }

    private void CacheTextureMetadata(Texture texture)
    {
        Width = texture.Extent.X;
        Height = texture.Extent.Y;
        Format = texture.Format;
        MipLevels = texture.MipLevels;
        IsWriteable = (texture.Desc.Usage & TextureUsage.TransferDestination) != 0;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (UploadLock)
        {
            if (_ownsResource && !_texture.IsDestroyed)
            {
                _texture.Destroy();
            }
        }
    }
}
