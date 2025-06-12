using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Alco.IO;

/// <summary>
/// A file source that reads from a ZIP archive.
/// This implementation is thread-safe.
/// </summary>
public class ZipFileSource : AutoDisposable, IFileSource
{
    private readonly ConcurrentDictionary<string, ZipArchiveEntry> _entries = new ConcurrentDictionary<string, ZipArchiveEntry>();
    private readonly ZipArchive _zipArchive;

    public string Name { get; }

    /// <summary>
    /// Load zip with file
    /// </summary>
    /// <param name="zipFilePath">The path to the ZIP file.</param>
    public ZipFileSource(string zipFilePath)
    {
        Name = zipFilePath;

        try
        {
            _zipArchive = ZipFile.OpenRead(zipFilePath);
            LoadZipEntries();
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to open ZIP file: {zipFilePath}", ex);
        }
    }

    public ZipFileSource(Stream stream, string name = "unnamed_zip_stream")
    {
        Name = name;
        try
        {
            _zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            LoadZipEntries();
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to open ZIP stream: {name}", ex);
        }
    }

    private void LoadZipEntries()
    {
        foreach (var entry in _zipArchive.Entries)
        {
            // Normalize path separators to forward slashes
            string normalizedPath = entry.FullName.Replace('\\', '/');

            // Skip directory entries
            if (!string.IsNullOrEmpty(normalizedPath) && !normalizedPath.EndsWith('/'))
            {
                _entries[normalizedPath] = entry;
            }
        };
    }

    /// <summary>
    /// Gets the priority of this file source.
    /// </summary>
    public int Priority => 0;

    /// <summary>
    /// Gets all file names in this file source.
    /// </summary>
    public IEnumerable<string> AllFileNames => _entries.Keys;

    /// <summary>
    /// Tries to get data from this file source.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="data">The data of the file.</param>
    /// <param name="failureReason">The failure reason.</param>
    /// <returns>True if the data is successfully retrieved, false otherwise.</returns>
    public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason)
    {
        if (IsDisposed)
        {
            data = SafeMemoryHandle.Empty;
            failureReason = "ZipFileSource has been disposed";
            return false;
        }

        try
        {
            // Normalize path separators
            string normalizedPath = path.Replace('\\', '/');

            if (_entries.TryGetValue(normalizedPath, out var entry))
            {
                using var stream = entry.Open();
                long uncompressedSize = entry.Length;
                data = new SafeMemoryHandle(uncompressedSize);
                stream.ReadExactly(data.AsSpan());
                failureReason = null;
                return true;
            }

            data = SafeMemoryHandle.Empty;
            failureReason = $"File not found in ZIP archive: {normalizedPath}";
            return false;
        }
        catch (Exception ex)
        {
            data = SafeMemoryHandle.Empty;
            failureReason = ex.ToString();
            return false;
        }
    }

    /// <summary>
    /// Disposes the resources used by the <see cref="ZipFileSource"/>.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        _zipArchive?.Dispose();
        _entries.Clear();
    }
}
