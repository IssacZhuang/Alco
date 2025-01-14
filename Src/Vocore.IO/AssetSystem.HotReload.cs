using System.Collections.Concurrent;

namespace Vocore.IO;

public sealed partial class AssetSystem
{
    private readonly ConcurrentDictionary<Type, object> _hotReloaders = new();

    /// <summary>
    /// Register a hot reloader for a specific asset type
    /// </summary>
    /// <param name="hotReloader">The hot reloader implementation</param>
    public void RegisterAssetHotReloader(Type type, IAssetHotReloader hotReloader)
    {
        _hotReloaders[type] = hotReloader;
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
            IAssetHotReloader assetHotReloader = (IAssetHotReloader)hotReloader;

            try
            {
                assetHotReloader.HotReload(cachedAsset, data);
            }
            catch (Exception ex)
            {
                failedReason = $"Failed to hot reload asset {filename}: {ex.Message}";
                return false;
            }
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