using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Alco.IO;

internal static class DiskFileUtility
{
    public delegate bool TryWriteAction(IRandomAccessHandle handle, [NotNullWhen(false)] out string? failureReason);

    public static bool TryOpenRead(string fullPath, [NotNullWhen(true)] out IRandomAccessHandle? handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        string resolved = Path.GetFullPath(fullPath);
        if (!File.Exists(resolved))
        {
            handle = null;
            return false;
        }

        try
        {
            SafeFileHandle fileHandle = File.OpenHandle(
                resolved,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.RandomAccess);
            handle = new SafeFileRandomAccessHandle(fileHandle, writable: false);
            return true;
        }
        catch (FileNotFoundException)
        {
            handle = null;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            handle = null;
            return false;
        }
    }

    public static IRandomAccessHandle OpenAppendOrCreate(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        string resolved = Path.GetFullPath(fullPath);

        SafeFileHandle fileHandle;
        try
        {
            fileHandle = File.OpenHandle(
                resolved,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.RandomAccess);
        }
        catch (DirectoryNotFoundException)
        {
            EnsureParentDirectoryExists(resolved);
            fileHandle = File.OpenHandle(
                resolved,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.RandomAccess);
        }

        return new SafeFileRandomAccessHandle(fileHandle, writable: true);
    }

    public static void WriteAtomic(string fullPath, ReadOnlySpan<byte> data, bool flushToDisk = true)
    {
        (string resolved, string tempPath, SafeFileRandomAccessHandle handle) = BeginAtomicTempWrite(fullPath);
        try
        {
            if (!data.IsEmpty)
                handle.WriteAt(0, data);

            CommitAtomicTempWrite(resolved, tempPath, handle, flushToDisk);
        }
        catch
        {
            RollbackAtomicTempWrite(tempPath, handle);
            throw;
        }
    }

    public static bool TryWriteAtomic(
        string fullPath,
        TryWriteAction writeAction,
        [NotNullWhen(false)] out string? failureReason,
        bool flushToDisk = true)
    {
        ArgumentNullException.ThrowIfNull(writeAction);
        (string resolved, string tempPath, SafeFileRandomAccessHandle handle) = BeginAtomicTempWrite(fullPath);
        try
        {
            if (!writeAction(handle, out failureReason))
            {
                RollbackAtomicTempWrite(tempPath, handle);
                return false;
            }

            CommitAtomicTempWrite(resolved, tempPath, handle, flushToDisk);
            return true;
        }
        catch
        {
            RollbackAtomicTempWrite(tempPath, handle);
            throw;
        }
    }

    private static (string ResolvedPath, string TempPath, SafeFileRandomAccessHandle Handle) BeginAtomicTempWrite(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        string resolved = Path.GetFullPath(fullPath);
        string tempPath = $"{resolved}.{Guid.NewGuid():N}.tmp";

        SafeFileHandle fileHandle;
        try
        {
            fileHandle = File.OpenHandle(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                FileOptions.None);
        }
        catch (DirectoryNotFoundException)
        {
            EnsureParentDirectoryExists(resolved);
            fileHandle = File.OpenHandle(
                tempPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                FileOptions.None);
        }

        return (resolved, tempPath, new SafeFileRandomAccessHandle(fileHandle, writable: true));
    }

    private static void CommitAtomicTempWrite(
        string resolvedPath,
        string tempPath,
        SafeFileRandomAccessHandle handle,
        bool flushToDisk)
    {
        if (flushToDisk)
            handle.FlushToDisk();

        handle.Dispose();
        File.Move(tempPath, resolvedPath, overwrite: true);
    }

    private static void RollbackAtomicTempWrite(string tempPath, SafeFileRandomAccessHandle handle)
    {
        try
        {
            handle.Dispose();
        }
        catch
        {
            // Preserve caller failure context.
        }

        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Preserve caller failure context.
        }
    }

    private static void EnsureParentDirectoryExists(string resolvedFilePath)
    {
        string? parentDir = Path.GetDirectoryName(resolvedFilePath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);
    }

    private sealed class SafeFileRandomAccessHandle(SafeFileHandle handle, bool writable) : IRandomAccessHandle
    {
        private readonly SafeFileHandle _handle = handle;
        private readonly bool _writable = writable;
        private volatile bool _disposed;

        public long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return RandomAccess.GetLength(_handle);
            }
        }

        public int ReadAt(long offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (destination.IsEmpty)
                return 0;

            int totalRead = 0;
            while (totalRead < destination.Length)
            {
                int read = RandomAccess.Read(_handle, destination[totalRead..], offset + totalRead);
                if (read == 0)
                    break;

                totalRead += read;
            }

            return totalRead;
        }

        public void WriteAt(long offset, ReadOnlySpan<byte> source)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_writable)
                throw new NotSupportedException("Handle is read-only.");

            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (source.IsEmpty)
                return;

            RandomAccess.Write(_handle, source, offset);
        }

        public void FlushToDisk()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_writable)
                return;

            RandomAccess.FlushToDisk(_handle);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _handle.Dispose();
        }
    }
}
