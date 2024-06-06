using System.Collections.Concurrent;

namespace Vocore.IO;

internal class AssetWatcher : IDisposable
{
    private ConcurrentQueue<string> _changedFiles;
    private Action<string> _onAssetChanged;
    private List<FileSystemWatcher> _watchers;


    public AssetWatcher(Action<string> OnAssetChanged)
    {
        _onAssetChanged = OnAssetChanged;
        _changedFiles = new ConcurrentQueue<string>();
        _watchers = new List<FileSystemWatcher>();
    }

    public void Watch(string path)
    {
        var watcher = new FileSystemWatcher
        {
            Path = path,
            NotifyFilter = NotifyFilters.LastWrite,
            Filter = "*.*",
            IncludeSubdirectories = true
        };


        watcher.Changed += OnChanged;
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    // run on main thread to process changes
    public void Update()
    {
        while (_changedFiles.TryDequeue(out var file))
        {
            Log.Info($"File changed: {file}");
            _onAssetChanged(file);
        }
    }

    private void OnChanged(object source, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Changed)
        {
            _changedFiles.Enqueue(e.FullPath);
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }
}
