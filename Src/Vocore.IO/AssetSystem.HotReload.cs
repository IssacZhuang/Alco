using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Vocore.IO;

public sealed partial class AssetSystem
{
    private readonly ConcurrentDictionary<Type, object> _hotReloaders = new();
    private readonly ConcurrentDictionary<string, Task<SafeMemoryHandle?>> _hotReloadIOTask = new();

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

    /// <summary>
    /// Enqueue a hot reload task for a specific asset
    /// </summary>
    /// <param name="filename">The asset filename</param>
    public void EnqueueHotReload(string filename)
    {
        string extension = Path.GetExtension(filename);
        if (!IsRecongizedExtension(extension))
        {
            return;
        }

        // Don't start new IO task if one is already pending
        if (_hotReloadIOTask.ContainsKey(filename))
        {
            return;
        }

        AssetHandle handle = GetAssetHandle(filename);
        if (handle.CachedAsset is null)
        {
            return;
        }

        if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
        {
            var task = GetHotReloadIOTask(filename, fileSource);
            _hotReloadIOTask[filename] = task;
        }
        else
        {
            _host.LogError($"File source for {filename} not found");
        }
    }

    private void HotReload(string filename, ReadOnlySpan<byte> data)
    {
        AssetHandle handle = GetAssetHandle(filename);
        object? cachedAsset = handle.CachedAsset;
        if (cachedAsset is null)
        {
            return;
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

    private async Task<SafeMemoryHandle?> GetHotReloadIOTask(string filename, IFileSource fileSource)
    {
        int attempt = 10;
        while (attempt > 0)
        {
            if (fileSource.TryGetData(filename, out SafeMemoryHandle data, out string? failureReason))
            {
                return data;
            }
            await Task.Delay(100);
            attempt--;
        }
        return null;
    }

    private void ProcessHotReloadQueue()
    {
        foreach (var entry in _hotReloadIOTask.ToArray())
        {
            string filename = entry.Key;
            var pendingTask = entry.Value;

            if (!pendingTask.IsCompleted)
            {
                continue;
            }

            _hotReloadIOTask.TryRemove(filename, out _);

            AssetHandle handle = GetAssetHandle(filename);
            object? cachedAsset = handle.CachedAsset;
            if (cachedAsset is null)
            {
                continue;
            }

            try
            {
                var data = pendingTask.Result;
                if (data != null)
                {
                    HotReload(filename, data.Span);
                    OnHotReload?.Invoke(filename, cachedAsset);
                    data.Dispose();
                    _host.LogSuccess($"Hot reload asset {filename} success");
                }
            }
            catch (Exception ex)
            {
                _host.LogError($"Failed to hot reload asset {filename}: {ex}");
            }
        }
    }
}