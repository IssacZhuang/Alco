using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Abstract base for lazily discovering, merging, and caching directory-level option files.
/// Thread-safe. O(1) lookup after first computation per directory.
/// </summary>
/// <typeparam name="TOption">The option model type.</typeparam>
public abstract class DirectoryOptionCache<TOption> where TOption : class, new()
{
    private readonly AssetSystem _assetSystem;
    private readonly ConcurrentDictionary<string, TOption> _mergedCache = new();
    private Dictionary<string, TOption>? _rawOptions;

    /// <summary>
    /// The option file name to scan for (e.g., ".texture-option.meta").
    /// </summary>
    protected abstract string OptionFileName { get; }

    /// <summary>
    /// Merges a parent option with a child option, where child non-null fields override parent.
    /// </summary>
    protected abstract TOption MergeOptions(TOption parent, TOption child);

    /// <summary>
    /// Creates the <see cref="JsonSerializerOptions"/> used for deserializing option files.
    /// </summary>
    protected abstract JsonSerializerOptions CreateJsonOptions();

    /// <summary>
    /// Initializes a new instance with the given <see cref="AssetSystem"/>.
    /// </summary>
    /// <param name="assetSystem">The asset system used for file discovery and loading.</param>
    protected DirectoryOptionCache(AssetSystem assetSystem)
    {
        _assetSystem = assetSystem;
    }

    /// <summary>
    /// Attempts to get the cascade-merged option for a directory.
    /// Returns false if no option files exist in this directory's ancestry.
    /// </summary>
    /// <param name="directory">The directory path to look up.</param>
    /// <param name="option">The merged option, if found.</param>
    /// <returns>True if an option was found; otherwise, false.</returns>
    public bool TryGetOption(string directory, [NotNullWhen(true)] out TOption? option)
    {
        EnsureRawOptionsLoaded();
        string normalizedDir = NormalizeDirectory(directory);

        if (_mergedCache.TryGetValue(normalizedDir, out option))
            return true;

        TOption? merged = BuildMergedOption(normalizedDir);
        if (merged == null)
        {
            option = null;
            return false;
        }

        option = _mergedCache.GetOrAdd(normalizedDir, merged);
        return true;
    }

    /// <summary>
    /// Clears cached data, forcing re-discovery on next access.
    /// </summary>
    public void Invalidate()
    {
        _rawOptions = null;
        _mergedCache.Clear();
    }

    /// <summary>
    /// Gets the <see cref="AssetSystem"/> for subclass use (e.g., loading per-file meta).
    /// </summary>
    protected AssetSystem AssetSystem => _assetSystem;

    private void EnsureRawOptionsLoaded()
    {
        if (_rawOptions != null)
            return;

        lock (this)
        {
            if (_rawOptions != null)
                return;

            var raw = new Dictionary<string, TOption>();
            JsonSerializerOptions jsonOptions = CreateJsonOptions();

            foreach (string filename in _assetSystem.AllAssetNames)
            {
                if (!filename.EndsWith(OptionFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_assetSystem.TryLoadRaw(filename, out var data))
                {
                    try
                    {
                        string json = System.Text.Encoding.UTF8.GetString(data.AsReadOnlySpan());
                        if (JsonSerializer.Deserialize<TOption>(json, jsonOptions) is TOption opt)
                        {
                            string dir = NormalizeDirectory(Path.GetDirectoryName(filename));
                            raw[dir] = opt;
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed option files
                    }
                    finally
                    {
                        data.Dispose();
                    }
                }
            }

            _rawOptions = raw;
        }
    }

    private TOption? BuildMergedOption(string directory)
    {
        var segments = new List<string>();
        string current = directory;
        while (!string.IsNullOrEmpty(current))
        {
            segments.Add(current);
            string? parent = NormalizeDirectory(Path.GetDirectoryName(current));
            if (parent == current)
                break;
            current = parent;
        }
        segments.Reverse();

        TOption? result = null;
        foreach (string seg in segments)
        {
            if (_rawOptions!.TryGetValue(seg, out var raw))
            {
                if (result == null)
                    result = raw;
                else
                    result = MergeOptions(result, raw);
            }
        }

        return result;
    }

    private static string NormalizeDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        return path.Replace('\\', '/').TrimEnd('/');
    }
}
