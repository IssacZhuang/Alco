using System.Collections.Concurrent;

namespace Vocore.IO;

public sealed partial class AssetSystem
{
    private readonly ConcurrentDictionary<string, byte> _hotReloadSet = new();
    private readonly ConcurrentDictionary<Type, object> _hotReloaders = new();

    public event Action<string, object>? OnHotReload;

    /// <summary>
    /// Register a hot reloader for a specific asset type
    /// </summary>
    /// <typeparam name="TAsset">The asset type</typeparam>
    /// <param name="hotReloader">The hot reloader implementation</param>
    public void RegisterAssetHotReloader<TAsset>(IAssetHotReloader hotReloader) where TAsset : class
    {
        _hotReloaders[typeof(TAsset)] = hotReloader;
    }

    public void EnqueueHotReload(string filename)
    {
        string extension = Path.GetExtension(filename);
        if (IsRecongizedExtension(extension))
        {
            _hotReloadSet.TryAdd(filename, 0);
        }
    }

    private void HotReload(string filename, ReadOnlySpan<byte> data)
    {
        AssetHandle handle = GetAssetHandle(filename);
        object? cachedAsset = handle.CachedAsset;
        if (cachedAsset is null)
        {
            throw new Exception($"Asset {filename} is not loaded");
        }

        if (_hotReloaders.TryGetValue(cachedAsset.GetType(), out object? hotReloader))
        {
            IAssetHotReloader assetHotReloader = (IAssetHotReloader)hotReloader;
            assetHotReloader.HotReload(cachedAsset, data);
        }
        else
        {
            throw new Exception($"No hot reloader found for type {cachedAsset.GetType().Name}");
        }
    }


    private void ProcessHotReloadQueue()
    {
        foreach (var entry in _hotReloadSet.ToArray())
        {
            string filename = entry.Key;
            _hotReloadSet.TryRemove(filename, out _);

            AssetHandle handle = GetAssetHandle(filename);
            object? cachedAsset = handle.CachedAsset;
            if (cachedAsset is null)
            {
                continue;
            }

            if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
            {
                //it might be IO failed because the file might be accessed by other process
                try
                {
                    if (fileSource.TryGetData(filename, out ReadOnlySpan<byte> data))
                    {
                        HotReload(filename, data);
                        OnHotReload?.Invoke(filename, cachedAsset);
                    }

                    _host.LogSuccess($"Hot reload asset {filename} success");
                }
                catch (Exception ex)
                {
                    _host.LogError($"Failed to hot reload asset {filename}: {ex}");
                }
            }
            else
            {
                _host.LogError($"File source for {filename} not found");
            }
        }
    }
}