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
    /// <summary>
    /// A stream for reading package entries.
    /// Provides seekable access to individual files within a package.
    /// </summary>
    private sealed class PackageEntryStream : Stream
    {
        private readonly PackageReader _reader;
        private readonly PackageEntry _entry;
        private long _position;

        public PackageEntryStream(PackageReader reader, PackageEntry entry)
        {
            _reader = reader;
            _entry = entry;
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _entry.Size;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _entry.Size)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _entry.Size)
                return 0;

            int bytesToRead = (int)Math.Min(count, _entry.Size - _position);
            if (bytesToRead == 0)
                return 0;

            _reader.ReadByEntry(_entry, buffer.AsSpan(offset, bytesToRead), _position);
            _position += bytesToRead;
            return bytesToRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    _position = offset;
                    break;
                case SeekOrigin.Current:
                    _position += offset;
                    break;
                case SeekOrigin.End:
                    _position = _entry.Size + offset;
                    break;
            }

            if (_position < 0)
                _position = 0;
            else if (_position > _entry.Size)
                _position = _entry.Size;

            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

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

    public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
    {
        if (IsDisposed)
        {
            stream = null;
            failureReason = "PackageFileSource has been disposed";
            return false;
        }

        try
        {
            string normalizedPath = path.Replace('\\', '/');
            if (_reader.TryGetEntry(normalizedPath, out var entry))
            {
                stream = new PackageEntryStream(_reader, entry);
                failureReason = null;
                return true;
            }

            stream = null;
            failureReason = $"File not found in package: {normalizedPath}";
            return false;
        }
        catch (Exception ex)
        {
            stream = null;
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

