using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    private readonly struct HotReloadTask
    {
        public readonly Task<SafeMemoryHandle?> Task;
        public readonly CancellationTokenSource CancellationSource;

        public HotReloadTask(Task<SafeMemoryHandle?> task, CancellationTokenSource cancellationSource)
        {
            Task = task;
            CancellationSource = cancellationSource;
        }
    }

    private readonly ConcurrentDictionary<Type, IAssetHotReloader> _hotReloaders = new();
    private readonly ConcurrentDictionary<string, HotReloadTask> _hotReloadTasks = new();

    public event Action<string, object>? OnHotReload;

    /// <summary>
    /// Register a hot reloader for a specific asset type
    /// </summary>
    /// <param name="hotReloader">The hot reloader implementation</param>
    public void RegisterAssetHotReloader(IAssetHotReloader hotReloader)
    {
        ArgumentNullException.ThrowIfNull(hotReloader);
        foreach (var type in hotReloader.GetSupportedTypes())
        {
            _hotReloaders[type] = hotReloader;
        }
    }

    /// <summary>
    /// Unregister a hot reloader for a specific asset type
    /// </summary>
    /// <param name="hotReloader">The hot reloader implementation</param>
    public void UnregisterAssetHotReloader(IAssetHotReloader hotReloader)
    {
        foreach (var type in hotReloader.GetSupportedTypes())
        {
            if (_hotReloaders.TryGetValue(type, out var value) && ReferenceEquals(value, hotReloader))
            {
                _hotReloaders.TryRemove(type, out _);
            }
        }
    }

    /// <summary>
    /// Enqueue a hot reload task for a specific asset
    /// </summary>
    /// <param name="filename">The asset filename</param>
    public void EnqueueHotReload(string filename)
    {
        _host.PostToMainThread(() =>
        {
            filename = ParseEntry(filename);
            if (IsFileExist(filename, out string realFilename))
            {
                filename = realFilename;
            }

            // Cancel existing task if any
            if (_hotReloadTasks.TryGetValue(filename, out var existingTask))
            {
                existingTask.CancellationSource.Cancel();
                existingTask.CancellationSource.Dispose();
                _host.LogInfo($"Hot reload task for {filename} interrupted with a new task");
            }

            if (!_assetLookup.TryGetValue(filename, out AssetHandle? handle) || handle.CachedAsset is null)
            {
                return;
            }

            if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
            {
                var cts = new CancellationTokenSource();
                var task = GetHotReloadIOTask(filename, fileSource, cts.Token);
                HotReloadTask hotReloadTask = new HotReloadTask(task, cts);
                _hotReloadTasks[filename] = hotReloadTask;
                ProcessHotReloadTask(filename, hotReloadTask);
            }
            else
            {
                _host.LogError($"File source for {filename} not found");
            }
        });
    }

    private static async Task<SafeMemoryHandle?> GetHotReloadIOTask(string filename, IFileSource fileSource, CancellationToken cancellationToken)
    {
        int attempt = 10;
        while (attempt > 0 && !cancellationToken.IsCancellationRequested)
        {
            if (fileSource.TryGetData(filename, out SafeMemoryHandle data, out string? failureReason))
            {
                return data;
            }
            try
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            attempt--;
        }
        return null;
    }

    private async void ProcessHotReloadTask(string filename, HotReloadTask hotReloadTask)
    {
        using SafeMemoryHandle? data = await hotReloadTask.Task;

        if (data is null || hotReloadTask.CancellationSource.IsCancellationRequested)
        {
            RemoveTaskIfCurrent(filename, hotReloadTask);
            return;
        }

        if (!_assetLookup.TryGetValue(filename, out AssetHandle? handle) || handle.CachedAsset is not object cachedAsset)
        {
            RemoveTaskIfCurrent(filename, hotReloadTask);
            return;
        }

        Type assetType = cachedAsset.GetType();
        if (_hotReloaders.TryGetValue(assetType, out IAssetHotReloader? hotReloader))
        {
            hotReloader.HotReload(cachedAsset, data.AsReadOnlySpan());
            OnHotReload?.Invoke(filename, cachedAsset);
            RemoveTaskIfCurrent(filename, hotReloadTask);
            _host.LogSuccess($"Hot reload asset {filename} success");
            return;
        }

        // Replace the cache entry with a freshly loaded immutable instance without disposing
        // the prior asset so in-flight GPU commands or held references stay valid.
        data.Dispose();
        if (!TryGetLoader(filename, assetType, out IAssetLoader? loader))
        {
            RemoveTaskIfCurrent(filename, hotReloadTask);
            _host.LogError($"No asset loader or hot reloader found for {filename} ({assetType.Name})");
            return;
        }

        var newHandle = new AssetHandle();
        object? reloadedAsset;
        string failedReason;
        lock (newHandle)
        {
            if (!TryLoadAssetCore(filename, assetType, newHandle, loader, out reloadedAsset, out failedReason))
            {
                RemoveTaskIfCurrent(filename, hotReloadTask);
                _host.LogError($"Hot reload asset {filename} failed: {failedReason}");
                return;
            }

            newHandle.SetCache(reloadedAsset, handle.CacheMode);
        }

        if (_assetLookup.TryUpdate(filename, newHandle, handle))
        {
            OnHotReload?.Invoke(filename, reloadedAsset);
            _host.LogSuccess($"Hot reload asset {filename} success");
        }

        RemoveTaskIfCurrent(filename, hotReloadTask);
    }

    private void RemoveTaskIfCurrent(string filename, HotReloadTask expectedTask)
    {
        if (_hotReloadTasks.TryGetValue(filename, out var currentTask)
            && ReferenceEquals(currentTask.CancellationSource, expectedTask.CancellationSource))
        {
            _hotReloadTasks.TryRemove(filename, out _);
        }
    }
}