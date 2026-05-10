using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Abstract base for lazily discovering, merging, and caching directory-level option files.
/// Thread-safe. Option files are loaded per-directory on demand — no upfront scan.
/// Auto-invalidates when <see cref="AssetSystem.Version"/> changes.
/// </summary>
/// <typeparam name="TOption">The option model type.</typeparam>
public abstract class DirectoryOptionCache<TOption> where TOption : class, new()
{
    private readonly struct RawOption
    {
        public TOption? Value { get; }
        public bool Found { get; }
        private RawOption(TOption? value, bool found) { Value = value; Found = found; }
        public static RawOption Missing => new(null, false);
        public static RawOption Present(TOption value) => new(value, true);
    }

    private readonly AssetSystem _assetSystem;
    private readonly ConcurrentDictionary<string, RawOption> _rawOptions = new();
    private readonly ConcurrentDictionary<string, TOption> _mergedCache = new();
    private int _lastVersion;

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
        _lastVersion = assetSystem.Version;
    }

    /// <summary>
    /// Attempts to get the cascade-merged option for a directory.
    /// Returns false if no option files exist in this directory's ancestry.
    /// Auto-invalidates if <see cref="AssetSystem.Version"/> has changed.
    /// </summary>
    /// <param name="directory">The directory path to look up.</param>
    /// <param name="option">The merged option, if found.</param>
    /// <returns>True if an option was found; otherwise, false.</returns>
    public bool TryGetOption(string directory, [NotNullWhen(true)] out TOption? option)
    {
        int currentVersion = _assetSystem.Version;
        if (_lastVersion != currentVersion)
        {
            Invalidate();
            _lastVersion = currentVersion;
        }

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
        _rawOptions.Clear();
        _mergedCache.Clear();
    }

    /// <summary>
    /// Gets the <see cref="AssetSystem"/> for subclass use (e.g., loading per-file meta).
    /// </summary>
    protected AssetSystem AssetSystem => _assetSystem;

    private RawOption GetOrLoadRawOption(string directory)
    {
        return _rawOptions.GetOrAdd(directory, static (dir, self) =>
        {
            string path = string.IsNullOrEmpty(dir)
                ? self.OptionFileName
                : $"{dir}/{self.OptionFileName}";

            if (self._assetSystem.IsFileExist(path) &&
                self._assetSystem.TryLoadRaw(path, out var data))
            {
                try
                {
                    string json = System.Text.Encoding.UTF8.GetString(data.AsReadOnlySpan());
                    if (JsonSerializer.Deserialize<TOption>(json, self.CreateJsonOptions()) is TOption opt)
                        return RawOption.Present(opt);
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

            return RawOption.Missing;
        }, this);
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
            RawOption raw = GetOrLoadRawOption(seg);
            if (raw.Found)
            {
                result = result == null ? raw.Value : MergeOptions(result, raw.Value!);
            }
        }

        return result;
    }

    /// <summary>
    /// Normalizes a directory path to forward slashes with no trailing slash.
    /// </summary>
    protected static string NormalizeDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        return path.Replace('\\', '/').TrimEnd('/');
    }
}
