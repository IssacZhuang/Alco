using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Alco;

namespace Alco.IO;

/// <summary>
/// A file source backed by an Alco package file.
/// Uses PackageReader for concurrent, position-based reads.
/// </summary>
public sealed class PackageFileSource : AutoDisposable, IFileSource
{
    private readonly PackageReader _reader;
    private readonly string[] _allFileNames;

    public string Name { get; }

    public int Priority => 0;

    public IEnumerable<string> AllFileNames => _allFileNames;

    /// <summary>
    /// Opens a package file from disk.
    /// </summary>
    public PackageFileSource(string packagePath)
    {
        Name = packagePath;
        try
        {
            _reader = PackageReader.OpenFile(packagePath);
            _allFileNames = DecodeAllNamesFromFile(packagePath);
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
            _allFileNames = DecodeAllNamesFromBytes(bytes);
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
                int size = checked((int)entry!.Size);
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reader?.Dispose();
        }
    }

    private static string[] DecodeAllNamesFromFile(string path)
    {
        using FileStream fs = File.OpenRead(path);
        Span<byte> header = stackalloc byte[8];
        fs.ReadExactly(header);
        long metaLength = BinaryPrimitives.ReadInt64LittleEndian(header);
        if (metaLength < 0 || metaLength > int.MaxValue)
        {
            throw new InvalidDataException($"Invalid meta length: {metaLength}");
        }

        byte[] metaBytes = new byte[(int)metaLength];
        fs.ReadExactly(metaBytes);
        PackageMeta meta = BinaryParser.Decode<PackageMeta>(metaBytes);
        return meta.Entries.Select(e => e.Name.Replace('\\', '/')).ToArray();
    }

    private static string[] DecodeAllNamesFromBytes(byte[] data)
    {
        if (data.Length < 8)
        {
            throw new InvalidDataException("Package data too small to contain header.");
        }
        long metaLength = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(0, 8));
        if (metaLength < 0 || 8L + metaLength > data.LongLength)
        {
            throw new InvalidDataException("Invalid meta length or truncated package data.");
        }
        int metaLen = checked((int)metaLength);
        byte[] metaBytes = new byte[metaLen];
        Buffer.BlockCopy(data, 8, metaBytes, 0, metaLen);
        PackageMeta meta = BinaryParser.Decode<PackageMeta>(metaBytes);
        return meta.Entries.Select(e => e.Name.Replace('\\', '/')).ToArray();
    }
}

