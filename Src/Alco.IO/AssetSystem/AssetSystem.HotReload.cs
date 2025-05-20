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
    /// <typeparam name="TAsset">The asset type</typeparam>
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

        if (data is null)
        {
            return;
        }

        if (hotReloadTask.CancellationSource.IsCancellationRequested)
        {
            return;
        }

        AssetHandle handle = GetAssetHandle(filename);
        object? cachedAsset = handle.CachedAsset;
        if (cachedAsset is null)
        {
            return;
        }

        if (_hotReloaders.TryGetValue(cachedAsset.GetType(), out IAssetHotReloader? hotReloader))
        {
            hotReloader.HotReload(cachedAsset, data.Span);
            OnHotReload?.Invoke(filename, cachedAsset);
            _hotReloadTasks.TryRemove(filename, out _);
            _host.LogSuccess($"Hot reload asset {filename} success");
        }
        else
        {
            throw new Exception($"No hot reloader found for type {cachedAsset.GetType().Name}");
        }
    }
}