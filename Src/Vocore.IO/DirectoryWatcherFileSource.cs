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

    public int Order => 3;

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

    public bool TryGetData(string path, [NotNullWhen(true)] out ReadOnlySpan<byte> data)
    {
        try
        {
            data = File.ReadAllBytes(Path.Combine(_directoryPath, path));
            return true;
        }
        catch (Exception)
        {
            data = ReadOnlySpan<byte>.Empty;
            return false;
        }
    }

    private void OnChanged(object source, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Changed)
        {
            try
            {
                string relativePath = Path.GetRelativePath(_directoryPath, e.FullPath);
                relativePath = FixPath(relativePath);
                byte[] data = File.ReadAllBytes(e.FullPath);
                _assetSystem.TryHotReload(relativePath, data);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to hot reload {e.FullPath}: {ex.Message}");
            }
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