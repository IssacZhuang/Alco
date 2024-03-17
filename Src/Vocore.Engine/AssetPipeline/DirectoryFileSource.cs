using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Vocore.Engine;

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

    public virtual int Order => 3;

    public virtual bool TryGetData(string path, [NotNullWhen(true)] out byte[]? data)
    {
        try
        {
            data = File.ReadAllBytes(Path.Combine(_directoryPath, path));
            return true;
        }
        catch (Exception)
        {
            data = null;
            return false;
        }
    }

    private string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }
}
