using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Lazily discovers, merges, and caches directory-level option files.
/// Thread-safe. O(1) lookup after first population.
/// </summary>
/// <typeparam name="TOption">The option model type.</typeparam>
public class DirectoryOptionCache<TOption> where TOption : class, new()
{
    private readonly AssetSystem _assetSystem;
    private readonly string _optionFileName;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Func<TOption, TOption, TOption> _mergeFunc;

    private Dictionary<string, TOption>? _rawOptions;
    private readonly ConcurrentDictionary<string, TOption> _mergedCache = new();

    /// <summary>
    /// Creates a new directory option cache.
    /// </summary>
    /// <param name="assetSystem">The asset system used for file discovery and loading.</param>
    /// <param name="optionFileName">The exact file name to look for (e.g., ".texture-option.meta").</param>
    /// <param name="jsonConverters">JSON converters to use for deserialization.</param>
    /// <param name="mergeFunc">Function that merges child option over parent option, returning the merged result.</param>
    public DirectoryOptionCache(
        AssetSystem assetSystem,
        string optionFileName,
        IEnumerable<JsonConverter> jsonConverters,
        Func<TOption, TOption, TOption> mergeFunc)
    {
        _assetSystem = assetSystem;
        _optionFileName = optionFileName;
        _mergeFunc = mergeFunc;

        _jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            AllowTrailingCommas = true,
        };
        foreach (var converter in jsonConverters)
        {
            _jsonOptions.Converters.Add(converter);
        }
        _jsonOptions.MakeReadOnly();
    }

    /// <summary>
    /// Gets the merged option for the given directory path.
    /// </summary>
    /// <param name="directory">Directory path with forward slashes, no trailing slash.</param>
    /// <param name="option">The merged option if found.</param>
    /// <returns>True if a merged option exists for this directory.</returns>
    public bool TryGetOption(string directory, [NotNullWhen(true)] out TOption? option)
    {
        EnsureInitialized();
        return _mergedCache.TryGetValue(NormalizeDirectory(directory), out option);
    }

    /// <summary>
    /// Clears the merged cache, forcing re-scan on next access.
    /// </summary>
    public void Invalidate()
    {
        _rawOptions = null;
        _mergedCache.Clear();
    }

    private void EnsureInitialized()
    {
        if (_rawOptions != null)
            return;

        lock (this)
        {
            if (_rawOptions != null)
                return;

            var raw = new Dictionary<string, TOption>();

            // Discover all option files
            foreach (string filename in _assetSystem.AllAssetNames)
            {
                if (!filename.EndsWith(_optionFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Load raw bytes and deserialize
                if (_assetSystem.TryLoadRaw(filename, out var data))
                {
                    try
                    {
                        string json = System.Text.Encoding.UTF8.GetString(data.AsReadOnlySpan());
                        if (JsonSerializer.Deserialize<TOption>(json, _jsonOptions) is TOption opt)
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

            // Pre-compute merged options for every directory that contains textures
            var directories = new HashSet<string>(raw.Keys);
            foreach (string filename in _assetSystem.AllAssetNames)
            {
                string? ext = Path.GetExtension(filename);
                if (ext is not (".png" or ".jpg" or ".bmp" or ".tga" or ".hdr" or ".gif"))
                    continue;
                directories.Add(NormalizeDirectory(Path.GetDirectoryName(filename)));
            }

            foreach (string dir in directories)
            {
                var merged = BuildMergedOption(dir);
                if (merged != null)
                    _mergedCache[dir] = merged;
            }
        }
    }

    /// <summary>
    /// Builds the cascade-merged option for a directory by walking from root to leaf.
    /// </summary>
    private TOption? BuildMergedOption(string directory)
    {
        // Collect path segments from root to target directory
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

        // Merge from root to leaf
        TOption? result = null;
        foreach (string seg in segments)
        {
            if (_rawOptions!.TryGetValue(seg, out var raw))
            {
                if (result == null)
                    result = raw;
                else
                    result = _mergeFunc(result, raw);
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
