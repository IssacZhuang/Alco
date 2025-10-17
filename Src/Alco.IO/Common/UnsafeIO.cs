using Microsoft.Win32.SafeHandles;
using static Alco.UtilsMemory;

namespace Alco.IO;

public static unsafe class UnsafeIO
{
    public static byte* ReadFile(string path, FileShare fileShare, out int size)
    {
        byte* ptr = null;
        try
        {
            using SafeFileHandle fileHandle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                fileShare,
                FileOptions.Asynchronous
                );

            long fileLength = RandomAccess.GetLength(fileHandle);
            if (fileLength > int.MaxValue)
            {
                throw new IOException($"File {path} is too large (> 2GB), try use Stream instead.");
            }


            size = (int)fileLength;
            ptr = (byte*)Alloc(size);

            Span<byte> buffer = new Span<byte>(ptr, size);
            int bytesRead = RandomAccess.Read(fileHandle, buffer, 0);
            if (bytesRead != size)
            {
                throw new IOException($"Failed to read entire file. Expected {size} bytes but read {bytesRead}");
            }


            return ptr;
        }
        catch (Exception ex)
        {
            if (ptr != null)
            {
                Free(ptr);
            }
            throw new IOException($"Failed to read file {path}", ex);
        }
    }

    public static byte* ReadFile(string path, out int size)
    {
        return ReadFile(path, FileShare.Read, out size);
    }

    /// <summary>
    /// Gets the length of a file in bytes.
    /// </summary>
    /// <param name="path">The file path</param>
    /// <returns>The length of the file in bytes</returns>
    public static long GetFileLength(string path)
    {
        using SafeFileHandle fileHandle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.None // Synchronous operation for metadata
        );

        return RandomAccess.GetLength(fileHandle);
    }

    /// <summary>
    /// Reads a portion of a file into the provided buffer.
    /// </summary>
    /// <param name="path">The file path to read from</param>
    /// <param name="buffer">The buffer to read into</param>
    /// <param name="offset">The offset within the file to start reading from</param>
    /// <param name="length">The maximum number of bytes to read</param>
    /// <param name="fileShare">File share mode</param>
    /// <returns>The number of bytes actually read</returns>
    public static int ReadFilePartial(string path, Span<byte> buffer, long offset, int length, FileShare fileShare = FileShare.Read)
    {
        using SafeFileHandle fileHandle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            fileShare,
            FileOptions.Asynchronous
        );

        long fileLength = RandomAccess.GetLength(fileHandle);
        if (fileLength > int.MaxValue)
        {
            throw new IOException($"File {path} is too large (> 2GB), try use Stream instead.");
        }

        if (offset >= fileLength)
        {
            return 0;
        }

        int bytesToRead = Math.Min(length, (int)(fileLength - offset));
        if (bytesToRead == 0)
        {
            return 0;
        }

        int bytesRead = RandomAccess.Read(fileHandle, buffer[..bytesToRead], offset);
        return bytesRead;
    }
}