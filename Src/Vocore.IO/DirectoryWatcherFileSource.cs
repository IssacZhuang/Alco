using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Vocore.IO;

public class DirectoryWatcherFileSource : IFileSource
{
    private readonly string _directoryPath;
    private readonly AssetSystem _assetSystem;
    private readonly FileSystemWatcher _watcher;

    public DirectoryWatcherFileSource(string directoryPath, AssetSystem assetSystem)
    {
        _directoryPath = directoryPath;
        _assetSystem = assetSystem;

        _watcher = new FileSystemWatcher
        {
            Path = directoryPath,
            NotifyFilter = NotifyFilters.LastWrite,
            Filter = "*.*",
            IncludeSubdirectories = true
        };

        _watcher.Changed += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public int Priority => 10;

    public IEnumerable<string> AllFileNames
    {
        get
        {
            foreach (var file in Directory.EnumerateFiles(_directoryPath, "*", SearchOption.AllDirectories))
            {
                yield return FixPath(Path.GetRelativePath(_directoryPath, file));
            }
        }
    }

    public bool TryGetData(string path, [NotNullWhen(true)] out ReadOnlySpan<byte> data, out string? failureReason)
    {
        try
        {
            data = File.ReadAllBytes(Path.Combine(_directoryPath, path));
            failureReason = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            data = ReadOnlySpan<byte>.Empty;
            failureReason = e.ToString();
            return false;
        }
    }

    private void OnChanged(object source, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Changed)
        {
            string relativePath = Path.GetRelativePath(_directoryPath, e.FullPath);
            relativePath = FixPath(relativePath);
            _assetSystem.EnqueueHotReload(relativePath);
        }
    }

    private static string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}