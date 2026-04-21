using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

namespace Alco.IO;

/// <summary>
/// A file source that watches a directory for file changes and provides file data.
/// Automatically disposes the <see cref="FileSystemWatcher"/> via <see cref="AutoDisposable"/>.
/// </summary>
public class DirectoryWatcherFileSource : AutoDisposable, IFileSource
{
    private readonly string _directoryPath;
    private readonly AssetSystem _assetSystem;
    private readonly FileSystemWatcher _watcher;

    public string Name => _directoryPath;

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

    public unsafe bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            byte* ptr = UnsafeIO.ReadFile(Path.Combine(_directoryPath, path), FileShare.ReadWrite, out int size);
            data = new SafeMemoryHandle(ptr, size);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            data = SafeMemoryHandle.Empty;
            failureReason = e.ToString();
            return false;
        }
    }

    public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            string fullPath = Path.Combine(_directoryPath, path);
            stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            stream = null;
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

    protected override void Dispose(bool disposing)
    {
        _watcher.Dispose();
    }
}
