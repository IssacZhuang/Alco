using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Alco;

namespace Alco.IO;

/// <summary>
/// A file source backed by an Alco package file.
/// Uses PackageReader for concurrent, position-based reads.
/// </summary>
public sealed class PackageFileSource : AutoDisposable, IFileSource
{
    private readonly PackageReader _reader;

    public string Name { get; }

    public int Priority => 0;

    public IEnumerable<string> AllFileNames => _reader.AllFileNames;

    /// <summary>
    /// Opens a package file from disk.
    /// </summary>
    public PackageFileSource(string packagePath)
    {
        Name = packagePath;
        try
        {
            _reader = PackageReader.OpenFile(packagePath);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to open package: {packagePath}", ex);
        }
    }

    /// <summary>
    /// Opens a package from a stream by buffering it into memory.
    /// </summary>
    public PackageFileSource(Stream stream, string name = "unnamed_package_stream")
    {
        Name = name;
        try
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();
            _reader = PackageReader.OpenMemory(bytes);
        }
        catch (Exception ex)
        {
            throw new IOException($"Failed to open package stream: {name}", ex);
        }
    }

    public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason)
    {
        if (IsDisposed)
        {
            data = SafeMemoryHandle.Empty;
            failureReason = "PackageFileSource has been disposed";
            return false;
        }

        try
        {
            string normalizedPath = path.Replace('\\', '/');
            if (_reader.TryGetEntry(normalizedPath, out var entry))
            {
                int size = checked((int)entry.Size);
                data = new SafeMemoryHandle(size);
                _reader.ReadByEntry(entry, data.AsSpan());
                failureReason = null;
                return true;
            }

            data = SafeMemoryHandle.Empty;
            failureReason = $"File not found in package: {normalizedPath}";
            return false;
        }
        catch (Exception ex)
        {
            data = SafeMemoryHandle.Empty;
            failureReason = ex.ToString();
            return false;
        }
    }

    public bool TryGetDataLength(string path, out long length, [NotNullWhen(false)] out string? failureReason)
    {
        if (IsDisposed)
        {
            length = 0;
            failureReason = "PackageFileSource has been disposed";
            return false;
        }

        try
        {
            string normalizedPath = path.Replace('\\', '/');
            if (_reader.TryGetEntry(normalizedPath, out var entry))
            {
                length = entry.Size;
                failureReason = null;
                return true;
            }

            length = 0;
            failureReason = $"File not found in package: {normalizedPath}";
            return false;
        }
        catch (Exception ex)
        {
            length = 0;
            failureReason = ex.ToString();
            return false;
        }
    }

    public bool TryRead(string path, Span<byte> buffer, int offset, int length, out int bytesRead, [NotNullWhen(false)] out string? failureReason)
    {
        if (IsDisposed)
        {
            bytesRead = 0;
            failureReason = "PackageFileSource has been disposed";
            return false;
        }

        try
        {
            string normalizedPath = path.Replace('\\', '/');
            if (_reader.TryGetEntry(normalizedPath, out var entry))
            {
                bytesRead = _reader.ReadByEntry(entry, buffer, offset);
                failureReason = null;
                return true;
            }

            bytesRead = 0;
            failureReason = $"File not found in package: {normalizedPath}";
            return false;
        }
        catch (Exception ex)
        {
            bytesRead = 0;
            failureReason = ex.ToString();
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader?.Dispose();
        }
    }
}

