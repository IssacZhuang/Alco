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
    /// <typeparam name="TAsset">The asset type</typeparam>
    /// <param name="hotReloader">The hot reloader implementation</param>
    public void RegisterAssetHotReloader<TAsset>(IAssetHotReloader hotReloader) where TAsset : class
    {
        ArgumentNullException.ThrowIfNull(hotReloader);
        _hotReloaders[typeof(TAsset)] = hotReloader;
    }

    /// <summary>
    /// Unregister a hot reloader for a specific asset type
    /// </summary>
    /// <typeparam name="TAsset">The asset type</typeparam>
    /// <param name="hotReloader">The hot reloader implementation</param>
    public void UnregisterAssetHotReloader<TAsset>(IAssetHotReloader hotReloader) where TAsset : class
    {
        _hotReloaders.TryRemove(typeof(TAsset), out _);
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

        // Cancel existing task if any
        if (_hotReloadTasks.TryGetValue(filename, out var existingTask))
        {
            existingTask.CancellationSource.Cancel();
            existingTask.CancellationSource.Dispose();
            _host.LogInfo($"Hot reload task for {filename} interrupted with a new task");
        }

        AssetHandle handle = GetAssetHandle(filename);
        if (handle.CachedAsset is null)
        {
            return;
        }

        if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
        {
            var cts = new CancellationTokenSource();
            var task = GetHotReloadIOTask(filename, fileSource, cts.Token);
            _hotReloadTasks[filename] = new HotReloadTask(task, cts);
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

        if (_hotReloaders.TryGetValue(cachedAsset.GetType(), out IAssetHotReloader? hotReloader))
        {
            hotReloader.HotReload(cachedAsset, data);
        }
        else
        {
            throw new Exception($"No hot reloader found for type {cachedAsset.GetType().Name}");
        }
    }

    private async Task<SafeMemoryHandle?> GetHotReloadIOTask(string filename, IFileSource fileSource, CancellationToken cancellationToken)
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

    private void ProcessHotReloadQueue()
    {
        foreach (var entry in _hotReloadTasks.ToArray())
        {
            string filename = entry.Key;
            var hotReloadTask = entry.Value;

            if (!hotReloadTask.Task.IsCompleted)
            {
                continue;
            }

            _hotReloadTasks.TryRemove(filename, out _);
            hotReloadTask.CancellationSource.Dispose();

            AssetHandle handle = GetAssetHandle(filename);
            object? cachedAsset = handle.CachedAsset;
            if (cachedAsset is null)
            {
                continue;
            }

            try
            {
                var data = hotReloadTask.Task.Result;
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