using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Engine;

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
        return new Texture2DMeta
        {
            FilterMode = child.FilterMode ?? parent.FilterMode,
            AddressMode = child.AddressMode ?? parent.AddressMode,
            SlicePadding = child.SlicePadding ?? parent.SlicePadding,
        };
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
        string directory = GetDirectory(filename);
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
        {
            result = new Texture2DMeta
            {
                FilterMode = meta.FilterMode ?? result.FilterMode,
                AddressMode = meta.AddressMode ?? result.AddressMode,
                SlicePadding = meta.SlicePadding ?? result.SlicePadding,
            };
        }

        return (result, meta);
    }

    private static string GetDirectory(string filename)
    {
        string? dir = Path.GetDirectoryName(filename);
        if (string.IsNullOrEmpty(dir))
            return string.Empty;
        return dir.Replace('\\', '/').TrimEnd('/');
    }
}
