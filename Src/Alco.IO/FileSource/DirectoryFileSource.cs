using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

public class DirectoryFileSource : IFileSource
{
    private string _directoryPath;
    public string DirectoryPath => _directoryPath;

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

    public bool IsWriteable => true;

    public virtual unsafe bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, out string? failureReason)
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

    private string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

    public void Dispose()
    {
        //do nothing
    }

    public bool TryWriteData(string path, ReadOnlySpan<byte> data, out string failureReason)
    {
        try
        {
            string fullPath = Path.Combine(_directoryPath, path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(fullPath, data);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception e)
        {
            failureReason = e.ToString();
            return false;
        }
    }
}
