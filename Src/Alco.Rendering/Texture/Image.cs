using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Immutable 2D image asset wrapping a GPU <see cref="Alco.Graphics.Texture"/> alongside import settings,
/// named sprites, 9-slice borders, and initial streaming arrival status.
/// </summary>
public sealed class Image : AutoDisposable
{
    private readonly bool _ownsResource;
    private readonly Sprite _defaultSprite;
    private readonly FrozenDictionary<string, Sprite> _sprites;

    private volatile Task? _contentUpload;
    private volatile int _contentPresent;

    internal readonly Lock UploadLock = new();

    /// <summary>Gets the underlying GPU texture in <see cref="Alco.Graphics"/>.</summary>
    public Texture Texture { get; }

    /// <summary>Gets the asset or diagnostic name of this image.</summary>
    public string Name { get; }

    /// <summary>Gets the image width in texels.</summary>
    public uint Width { get; }

    /// <summary>Gets the image height in texels.</summary>
    public uint Height { get; }

    /// <summary>Gets the pixel format of the underlying GPU texture.</summary>
    public Format Format { get; }

    /// <summary>Gets the number of mip levels of the underlying GPU texture.</summary>
    public uint MipLevels { get; }

    /// <summary>Gets the texture filter mode specified by import metadata.</summary>
    public Filter FilterMode { get; }

    /// <summary>Gets the texture address mode specified by import metadata.</summary>
    public AddressMode AddressMode { get; }

    /// <summary>Gets the maximum sampler anisotropy specified by import metadata.</summary>
    public ushort Anisotropy { get; }

    /// <summary>Gets whether pixel RGB channels were premultiplied by alpha during import.</summary>
    public bool IsPremultiplied { get; }

    /// <summary>Gets the 9-slice padding, in texels, configured for this image.</summary>
    public Padding SlicePadding { get; }

    /// <summary>Gets the default sprite covering the full UV rectangle of this image.</summary>
    public Sprite DefaultSprite => _defaultSprite;

    /// <summary>Gets the read-only lookup of named sprites defined on this image.</summary>
    public IReadOnlyDictionary<string, Sprite> Sprites => _sprites;

    /// <summary>
    /// Gets a task that completes when streamed content has been issued to the GPU queue.
    /// It never faults; check <see cref="IsContentLoaded"/> to determine upload success.
    /// </summary>
    public Task ContentArrival => _contentUpload ?? Task.CompletedTask;

    /// <summary>
    /// Gets whether the image's texel content is present on the GPU. False only between
    /// a streaming creation and its initial in-place upload completion.
    /// </summary>
    public bool IsContentLoaded => _contentPresent != 0;

    /// <summary>
    /// Initializes an immutable <see cref="Image"/> asset over a GPU <see cref="Alco.Graphics.Texture"/>.
    /// </summary>
    /// <param name="texture">The backing GPU texture.</param>
    /// <param name="option">Optional import settings describing sampler, slice, and alpha properties.</param>
    /// <param name="sprites">Optional named sprite regions in pixel coordinates.</param>
    /// <param name="ownsResource">Whether disposing this asset destroys <paramref name="texture"/>.</param>
    public Image(
        Texture texture,
        ImageLoadOption? option = null,
        IReadOnlyDictionary<string, RectInt>? sprites = null,
        bool ownsResource = true)
        : this(texture, option, sprites, ownsResource, isContentLoaded: true)
    {
    }

    internal Image(
        Texture texture,
        ImageLoadOption? option,
        IReadOnlyDictionary<string, RectInt>? sprites,
        bool ownsResource,
        bool isContentLoaded)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ImageLoadOption resolved = option ?? ImageLoadOption.Default;

        Texture = texture;
        _ownsResource = ownsResource;
        Name = string.IsNullOrEmpty(resolved.Name) ? "unnamed_texture" : resolved.Name;
        Width = texture.Extent.X;
        Height = texture.Extent.Y;
        Format = texture.Format;
        MipLevels = texture.MipLevels;
        FilterMode = resolved.FilterMode;
        AddressMode = resolved.AddressMode;
        Anisotropy = resolved.Anisotropy == 0 ? (ushort)1 : resolved.Anisotropy;
        IsPremultiplied = resolved.PremultiplyAlpha;
        SlicePadding = resolved.SlicePadding;
        _contentPresent = isContentLoaded ? 1 : 0;

        _defaultSprite = new Sprite("default", texture, Rect.One, SlicePadding, FilterMode, IsPremultiplied, owner: this);
        _sprites = BuildSprites(texture, Width, Height, SlicePadding, FilterMode, IsPremultiplied, sprites, owner: this);
    }

    internal void SetContentUpload(Task upload)
    {
        _contentUpload = upload;
    }

    internal void MarkContentLoaded()
    {
        _contentPresent = 1;
    }

    /// <summary>Gets the sprite with the given name, returning false when it does not exist.</summary>
    /// <param name="name">The sprite name.</param>
    /// <param name="sprite">When this method returns, contains the found sprite, or <c>null</c>.</param>
    /// <returns><c>true</c> when a sprite with the given name exists; otherwise, <c>false</c>.</returns>
    public bool TryGetSprite(string name, [NotNullWhen(true)] out Sprite? sprite)
    {
        return _sprites.TryGetValue(name, out sprite);
    }

    /// <summary>Gets the sprite with the given name, falling back to <see cref="DefaultSprite"/> when absent.</summary>
    /// <param name="name">The sprite name.</param>
    /// <returns>The named sprite, or <see cref="DefaultSprite"/>.</returns>
    public Sprite GetSprite(string name)
    {
        return _sprites.TryGetValue(name, out Sprite? sprite) ? sprite : _defaultSprite;
    }

    /// <summary>Implicitly converts an <see cref="Image"/> to its <see cref="DefaultSprite"/>.</summary>
    /// <param name="image">The image asset to convert.</param>
    /// <returns>The default sprite covering the image.</returns>
    public static implicit operator Sprite(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image._defaultSprite;
    }

    private static FrozenDictionary<string, Sprite> BuildSprites(
        Texture texture,
        uint width,
        uint height,
        Padding slicePadding,
        Filter filterMode,
        bool isPremultiplied,
        IReadOnlyDictionary<string, RectInt>? sprites,
        Image owner)
    {
        if (sprites is not { Count: > 0 } || width == 0 || height == 0)
        {
            return FrozenDictionary<string, Sprite>.Empty;
        }

        var built = new Dictionary<string, Sprite>(sprites.Count, StringComparer.Ordinal);
        foreach (var (spriteName, pixelRect) in sprites)
        {
            Rect uvRect = pixelRect.Normalize(width, height);
            built[spriteName] = new Sprite(spriteName, texture, uvRect, slicePadding, filterMode, isPremultiplied, owner: owner);
        }
        return built.ToFrozenDictionary(StringComparer.Ordinal);
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
            if (_ownsResource && !Texture.IsDestroyed)
            {
                Texture.Destroy();
            }
        }
    }
}
