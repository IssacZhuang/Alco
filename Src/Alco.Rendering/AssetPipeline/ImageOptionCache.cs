using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Self-contained image option cache. Discovers <c>.texture-option.meta</c> files,
/// merges them via cascade inheritance, and provides per-file resolution that combines
/// directory options with individual <c>.meta</c> overrides.
/// </summary>
public class ImageOptionCache : DirectoryOptionCache<ImageMeta>
{
    private readonly IJsonTypeInfoResolver? _typeInfoResolver;

    /// <inheritdoc/>
    protected override string OptionFileName => ".texture-option.meta";

    /// <summary>
    /// Initializes a new instance with the given <see cref="AssetSystem"/>.
    /// </summary>
    /// <param name="assetSystem">The asset system used for file discovery and loading.</param>
    /// <param name="typeInfoResolver">
    /// Optional resolver supplying type metadata (a source generated JsonSerializerContext
    /// under NativeAOT); defaults to reflection.
    /// </param>
    public ImageOptionCache(AssetSystem assetSystem, IJsonTypeInfoResolver? typeInfoResolver = null) : base(assetSystem)
    {
        _typeInfoResolver = typeInfoResolver;
    }

    /// <inheritdoc/>
    protected override ImageMeta MergeOptions(ImageMeta parent, ImageMeta child)
    {
        // Sprites remain per-image and are defined only in individual .meta files.
        return ApplyOverrides(parent, child);
    }

    /// <inheritdoc/>
    protected override JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = _typeInfoResolver ?? new DefaultJsonTypeInfoResolver(),
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new JsonConverterPadding());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// Resolves the fully merged import option for an image file.
    /// Combines directory cascade options with per-file <c>.meta</c> overrides.
    /// </summary>
    /// <param name="filename">The image filename (e.g., "Textures/Structure/Wall.png").</param>
    /// <returns>
    /// A tuple containing merged import settings (or null) and raw per-file <see cref="ImageMeta"/> (or null).
    /// </returns>
    public (ImageMeta? Option, ImageMeta? Meta) Resolve(string filename)
    {
        string directory = NormalizeDirectory(Path.GetDirectoryName(filename));
        ImageMeta? dirOption = null;
        if (TryGetOption(directory, out var cached))
        {
            dirOption = cached;
        }

        ImageMeta? meta = null;
        if (AssetSystem.TryLoad<ImageMeta>(filename + ".meta", out var loaded, out _))
        {
            meta = loaded;
        }

        if (dirOption == null && meta == null)
        {
            return (null, null);
        }

        ImageMeta result = dirOption ?? new ImageMeta();
        if (meta != null)
        {
            result = ApplyOverrides(result, meta);
        }

        return (result, meta);
    }

    internal static (ImageLoadOption Option, IReadOnlyDictionary<string, RectInt>? Sprites) ResolveLoadParameters(
        ImageOptionCache? cache,
        string filename,
        bool includeSprites)
    {
        ImageLoadOption option = ImageLoadOption.Default with { Name = filename };
        if (cache == null)
        {
            return (option, null);
        }

        var (importOption, meta) = cache.Resolve(filename);
        if (importOption != null)
        {
            option = option with
            {
                FilterMode = importOption.FilterMode ?? option.FilterMode,
                AddressMode = importOption.AddressMode ?? option.AddressMode,
                Anisotropy = importOption.Anisotropy ?? option.Anisotropy,
                SlicePadding = importOption.SlicePadding ?? option.SlicePadding,
                PremultiplyAlpha = importOption.PremultiplyAlpha ?? option.PremultiplyAlpha,
            };
        }

        if (!includeSprites || meta?.Sprites is not { Count: > 0 } rawSprites)
        {
            return (option, null);
        }

        var sprites = new Dictionary<string, RectInt>(rawSprites.Count, StringComparer.Ordinal);
        foreach (var (name, rect) in rawSprites)
        {
            sprites[name] = rect;
        }
        return (option, sprites);
    }

    private static ImageMeta ApplyOverrides(ImageMeta baseMeta, ImageMeta overrides)
    {
        return new ImageMeta
        {
            FilterMode = overrides.FilterMode ?? baseMeta.FilterMode,
            AddressMode = overrides.AddressMode ?? baseMeta.AddressMode,
            Anisotropy = overrides.Anisotropy ?? baseMeta.Anisotropy,
            SlicePadding = overrides.SlicePadding ?? baseMeta.SlicePadding,
            PremultiplyAlpha = overrides.PremultiplyAlpha ?? baseMeta.PremultiplyAlpha,
        };
    }
}
