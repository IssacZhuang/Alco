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

    /// <summary>The absolute or relative path of the watched root directory.</summary>
    public string DirectoryPath => _directoryPath;

    /// <summary>Gets the source name, which is the watched directory path.</summary>
    public string Name => _directoryPath;

    /// <summary>
    /// Initializes a new directory-watching file source.
    /// </summary>
    /// <param name="directoryPath">The root directory to watch and serve files from.</param>
    /// <param name="assetSystem">The asset system notified about changed files.</param>
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

    /// <summary>Gets the source priority; directory sources use 10.</summary>
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

    /// <summary>
    /// Reads a file's contents into an unmanaged buffer.
    /// </summary>
    /// <param name="path">The file path relative to the watched directory.</param>
    /// <param name="data">The loaded buffer on success; empty on failure.</param>
    /// <param name="failureReason">The failure detail on failure; empty on success.</param>
    /// <returns>True when the file was read; otherwise false.</returns>
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

    /// <summary>
    /// Opens a file as a readable stream.
    /// </summary>
    /// <param name="path">The file path relative to the watched directory.</param>
    /// <param name="stream">The opened stream on success; null on failure.</param>
    /// <param name="failureReason">The failure detail on failure; null on success.</param>
    /// <returns>True when the stream was opened; otherwise false.</returns>
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
