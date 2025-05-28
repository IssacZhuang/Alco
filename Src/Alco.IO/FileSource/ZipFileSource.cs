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
    private readonly bool _isWriteable;

    public string Name { get; }

    /// <summary>
    /// Load zip with file
    /// </summary>
    /// <param name="zipFilePath">The path to the ZIP file.</param>
    /// <param name="mode">The mode to open the ZIP file. Default is Read for backward compatibility.</param>
    public ZipFileSource(string zipFilePath, ZipArchiveMode mode = ZipArchiveMode.Read)
    {
        Name = zipFilePath;
        _isWriteable = mode == ZipArchiveMode.Update || mode == ZipArchiveMode.Create;

        try
        {
            _zipArchive = mode == ZipArchiveMode.Read
                ? ZipFile.OpenRead(zipFilePath)
                : ZipFile.Open(zipFilePath, mode);
            LoadZipEntries();
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to open ZIP file: {zipFilePath}", ex);
        }
    }

    /// <summary>
    /// Load zip from stream
    /// </summary>
    /// <param name="stream">The stream containing the ZIP data.</param>
    /// <param name="mode">The mode to open the ZIP archive. Default is Read for backward compatibility.</param>
    /// <param name="name">The name for this file source.</param>
    public ZipFileSource(Stream stream, ZipArchiveMode mode = ZipArchiveMode.Read, string name = "unnamed_zip_stream")
    {
        Name = name;
        _isWriteable = mode == ZipArchiveMode.Update || mode == ZipArchiveMode.Create;

        try
        {
            _zipArchive = new ZipArchive(stream, mode);
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
    /// Gets a value indicating whether this file source is writeable.
    /// </summary>
    public bool IsWriteable => _isWriteable;

    /// <summary>
    /// Tries to get data from this file source.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="data">The data of the file.</param>
    /// <param name="failureReason">The failure reason.</param>
    /// <returns>True if the data is successfully retrieved, false otherwise.</returns>
    public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, out string? failureReason)
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
    /// Tries to write data to this file source.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="data">The data of the file.</param>
    /// <param name="failureReason">The failure reason.</param>
    /// <returns>True if the data is successfully written, false otherwise.</returns>
    public bool TryWriteData(string path, ReadOnlySpan<byte> data, [NotNullWhen(false)] out string? failureReason)
    {
        if (IsDisposed)
        {
            failureReason = "ZipFileSource has been disposed";
            return false;
        }

        if (!_isWriteable)
        {
            failureReason = "ZIP archive is read-only";
            return false;
        }

        try
        {
            // Normalize path separators
            string normalizedPath = path.Replace('\\', '/');

            if (!_entries.TryGetValue(normalizedPath, out var entry))
            {
                entry = _zipArchive.CreateEntry(normalizedPath, CompressionLevel.Optimal);
            }

            // Write data to the entry
            using (var entryStream = entry.Open())
            {
                entryStream.Write(data);
            }

            // Update our cache
            _entries[normalizedPath] = entry;

            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
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
