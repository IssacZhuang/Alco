using Microsoft.Win32.SafeHandles;
using static Alco.Unsafe.UtilsMemory;

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
}