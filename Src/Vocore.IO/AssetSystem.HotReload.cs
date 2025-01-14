using System.Collections.Concurrent;

namespace Vocore.IO;

public sealed partial class AssetSystem
{
    private readonly ConcurrentDictionary<Type, object> _hotReloaders = new();

    /// <summary>
    /// Register a hot reloader for a specific asset type
    /// </summary>
    /// <param name="hotReloader">The hot reloader implementation</param>
    public void RegisterHotReloader(IAssetHotReloader hotReloader)
    {
        _hotReloaders[hotReloader.GetType()] = hotReloader;
    }

    public bool TryHotReload(string filename, ReadOnlySpan<byte> data, out string? failedReason)
    {
        AssetHandle handle = GetAssetHandle(filename);
        object? cachedAsset = handle.CachedAsset;
        if (cachedAsset is null)
        {
            failedReason = $"Asset {filename} is not loaded";
            return false;
        }

        if (_hotReloaders.TryGetValue(cachedAsset.GetType(), out object? hotReloader))
        {
            if (hotReloader is not IAssetHotReloader assetHotReloader)
            {
                failedReason = $"Hot reloader for type {cachedAsset.GetType().Name} is not an instance of IAssetHotReloader";
                return false;
            }

            assetHotReloader.TryHotReload(cachedAsset, data);
            failedReason = string.Empty;
            return true;
        }

        failedReason = $"No hot reloader found for type {cachedAsset.GetType().Name}";
        return false;
    }

    public bool TryHotReload(string filename, ReadOnlySpan<byte> data)
    {
        return TryHotReload(filename, data, out _);
    }
}