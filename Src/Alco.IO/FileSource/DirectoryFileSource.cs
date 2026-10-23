using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Win32.SafeHandles;

namespace Alco.IO;

/// <summary>
/// A file source backed by a directory on disk.
/// </summary>
public class DirectoryFileSource : IFileSource
{
    private readonly string _directoryPath;

    /// <summary>Gets the root directory path.</summary>
    public string DirectoryPath => _directoryPath;

    /// <summary>Gets the source name, which is the root directory path.</summary>
    public string Name => _directoryPath;

    /// <summary>Creates a file source rooted at the specified directory.</summary>
    /// <param name="directoryPath">The root directory path.</param>
    public DirectoryFileSource(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public virtual IEnumerable<string> AllFileNames
    {
        get
        {
            //missing directories serve no files instead of throwing, so consumers can
            //mount optional roots (e.g. a default relative "Assets" folder that may not exist)
            if (!Directory.Exists(_directoryPath))
            {
                yield break;
            }

            //list all files in directory or sub directory with relative path
            foreach (var file in Directory.EnumerateFiles(_directoryPath, "*", SearchOption.AllDirectories))
            {
                yield return FixPath(Path.GetRelativePath(_directoryPath, file));
            }
        }
    }

    /// <summary>Gets the resolution priority; higher-priority sources override lower-priority ones.</summary>
    public virtual int Priority => 5;

    /// <summary>Tries to load a file's data from the directory.</summary>
    /// <param name="path">The directory-relative file path.</param>
    /// <param name="data">The loaded data when successful.</param>
    /// <param name="failureReason">The reason for failure, when unsuccessful.</param>
    /// <returns><see langword="true"/> when the data was loaded; otherwise, <see langword="false"/>.</returns>
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

    /// <summary>Tries to open a read stream for a file in the directory.</summary>
    /// <param name="path">The directory-relative file path.</param>
    /// <param name="stream">The opened stream when successful; the caller owns it.</param>
    /// <param name="failureReason">The reason for failure, when unsuccessful.</param>
    /// <returns><see langword="true"/> when the stream was opened; otherwise, <see langword="false"/>.</returns>
    public virtual bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            string fullPath = Path.Combine(_directoryPath, path);
            stream = File.OpenRead(fullPath);
            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            stream = null;
            failureReason = e.ToString();
            return false;
        }
    }

    private string FixPath(string path)
    {
        return path.Replace('\\', '/');
    }

    /// <summary>Releases resources used by this source.</summary>
    public void Dispose()
    {
        //do nothing
    }
}
