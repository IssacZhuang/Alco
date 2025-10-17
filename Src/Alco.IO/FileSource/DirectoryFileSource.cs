using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

namespace Alco.IO;

public class DirectoryFileSource : IFileSource
{
    private readonly string _directoryPath;

    public string DirectoryPath => _directoryPath;

    public string Name => _directoryPath;

    public DirectoryFileSource(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public virtual IEnumerable<string> AllFileNames
    {
        get
        {
            //list all files in directory or sub directory with relative path
            foreach (var file in Directory.EnumerateFiles(_directoryPath, "*", SearchOption.AllDirectories))
            {
                yield return FixPath(Path.GetRelativePath(_directoryPath, file));
            }
        }
    }

    public virtual int Priority => 5;

    public virtual unsafe bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            byte* ptr = UnsafeIO.ReadFile(Path.Combine(_directoryPath, path), out int size);
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

    public virtual bool TryGetDataLength(string path, out long length, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            string fullPath = Path.Combine(_directoryPath, path);
            length = UnsafeIO.GetFileLength(fullPath);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            length = 0;
            failureReason = e.ToString();
            return false;
        }
    }

    public virtual bool TryRead(string path, Span<byte> buffer, int offset, int length, out int bytesRead, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            string fullPath = Path.Combine(_directoryPath, path);
            bytesRead = UnsafeIO.ReadFilePartial(fullPath, buffer, offset, length);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            bytesRead = 0;
            failureReason = e.ToString();
            return false;
        }
    }

    private string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

    public void Dispose()
    {
        //do nothing
    }
}
