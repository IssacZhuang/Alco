using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Self-contained texture option cache. Discovers <c>.texture-option.meta</c> files,
/// merges them via cascade inheritance, and provides per-file resolution that combines
/// directory options with individual <c>.meta</c> overrides.
/// </summary>
public class TextureOptionCache : DirectoryOptionCache<Texture2DMeta>
{
    /// <inheritdoc/>
    protected override string OptionFileName => ".texture-option.meta";

    /// <summary>
    /// Initializes a new instance with the given <see cref="AssetSystem"/>.
    /// </summary>
    /// <param name="assetSystem">The asset system used for file discovery and loading.</param>
    public TextureOptionCache(AssetSystem assetSystem) : base(assetSystem) { }

    /// <inheritdoc/>
    protected override Texture2DMeta MergeOptions(Texture2DMeta parent, Texture2DMeta child)
    {
        // Sprites is intentionally excluded from cascade merging — sprites remain
        // per-texture and are defined only in individual .meta files.
        return ApplyOverrides(parent, child);
    }

    /// <inheritdoc/>
    protected override JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            AllowTrailingCommas = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new JsonConverterPadding());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// Resolves the fully merged import option for a texture file.
    /// Combines directory cascade options with per-file <c>.meta</c> overrides.
    /// </summary>
    /// <param name="filename">The texture filename (e.g., "Textures/Structure/Wall.png").</param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    /// <item><term>Option</term> Merged import settings from directory cascade + .meta. Null if neither exists.</item>
    /// <item><term>Meta</term> Raw per-file Texture2DMeta (for Sprites). Null if no .meta exists.</item>
    /// </list>
    /// </returns>
    public (Texture2DMeta? Option, Texture2DMeta? Meta) Resolve(string filename)
    {
        // 1. Directory cascade option
        string directory = NormalizeDirectory(Path.GetDirectoryName(filename));
        Texture2DMeta? dirOption = null;
        if (TryGetOption(directory, out var cached))
            dirOption = cached;

        // 2. Per-file .meta
        Texture2DMeta? meta = null;
        if (AssetSystem.TryLoad<Texture2DMeta>(filename + ".meta", out var loaded, out _))
            meta = loaded;

        // 3. Merge: start from directory option, override with .meta non-null fields
        if (dirOption == null && meta == null)
            return (null, null);

        Texture2DMeta result = dirOption ?? new Texture2DMeta();
        if (meta != null)
            result = ApplyOverrides(result, meta);

        return (result, meta);
    }

    /// <summary>
    /// Creates a new <see cref="Texture2DMeta"/> where non-null fields from
    /// <paramref name="overrides"/> take precedence over <paramref name="baseMeta"/>.
    /// Sprites is not carried over — it is handled separately in per-file resolution.
    /// </summary>
    private static Texture2DMeta ApplyOverrides(Texture2DMeta baseMeta, Texture2DMeta overrides)
    {
        return new Texture2DMeta
        {
            FilterMode = overrides.FilterMode ?? baseMeta.FilterMode,
            AddressMode = overrides.AddressMode ?? baseMeta.AddressMode,
            SlicePadding = overrides.SlicePadding ?? baseMeta.SlicePadding,
            PremultiplyAlpha = overrides.PremultiplyAlpha ?? baseMeta.PremultiplyAlpha,
        };
    }
}
