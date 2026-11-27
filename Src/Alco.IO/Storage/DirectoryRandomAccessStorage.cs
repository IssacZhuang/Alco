using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace Alco.IO;

/// <summary>
/// Disk-backed <see cref="IRandomAccessStorage"/> implementation using random-access OS file handles.
/// </summary>
public sealed class DirectoryRandomAccessStorage : IRandomAccessStorage
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string _rootDirectory;
    private readonly string _rootPrefix;

    /// <summary>
    /// Gets the normalized full path to the storage root directory.
    /// </summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// Initializes a new directory-backed storage rooted at <paramref name="rootDirectory"/>.
    /// </summary>
    /// <param name="rootDirectory">The directory path on disk; created if it does not exist.</param>
    public DirectoryRandomAccessStorage(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        _rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    /// <inheritdoc/>
    public bool FileExists(string relativePath)
    {
        string fullPath = ResolveFilePath(relativePath);
        return File.Exists(fullPath);
    }

    /// <inheritdoc/>
    public bool TryOpenRead(string relativePath, [NotNullWhen(true)] out IRandomAccessHandle? handle)
    {
        string fullPath = ResolveFilePath(relativePath);
        return DiskFileUtility.TryOpenRead(fullPath, out handle);
    }

    /// <inheritdoc/>
    public IRandomAccessHandle OpenAppendOrCreate(string relativePath)
    {
        string fullPath = ResolveFilePath(relativePath);
        return DiskFileUtility.OpenAppendOrCreate(fullPath);
    }

    /// <inheritdoc/>
    public void WriteAtomic(string relativePath, ReadOnlySpan<byte> data, bool flushToDisk = true)
    {
        string fullPath = ResolveFilePath(relativePath);
        DiskFileUtility.WriteAtomic(fullPath, data, flushToDisk);
    }

    /// <inheritdoc/>
    public bool TryReadAllBytes(string relativePath, [NotNullWhen(true)] out byte[]? data)
    {
        if (!TryOpenRead(relativePath, out IRandomAccessHandle? handle))
        {
            data = null;
            return false;
        }

        using (handle)
        {
            long length = handle.Length;
            if (length > Array.MaxLength)
                throw new IOException($"File '{relativePath}' exceeds maximum array length.");

            int byteLength = (int)length;
            if (byteLength == 0)
            {
                data = [];
                return true;
            }

            byte[] buffer = new byte[byteLength];
            int totalRead = handle.ReadAt(0, buffer);
            data = totalRead == byteLength ? buffer : buffer[..totalRead];
            return true;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateFiles(string relativeDirectory, string searchPattern, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        string dirPath = ResolveDirectoryPath(relativeDirectory);
        if (!Directory.Exists(dirPath))
            return [];

        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<string>();
        foreach (string fullFilePath in Directory.EnumerateFiles(dirPath, searchPattern, option))
        {
            results.Add(Path.GetRelativePath(_rootDirectory, fullFilePath).Replace('\\', '/'));
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateDirectories(string relativeDirectory)
    {
        string dirPath = ResolveDirectoryPath(relativeDirectory);
        if (!Directory.Exists(dirPath))
            return [];

        var results = new List<string>();
        foreach (string fullSubDir in Directory.EnumerateDirectories(dirPath, "*", SearchOption.TopDirectoryOnly))
        {
            results.Add(Path.GetRelativePath(_rootDirectory, fullSubDir).Replace('\\', '/'));
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    /// <inheritdoc/>
    public bool DeleteFile(string relativePath)
    {
        string fullPath = ResolveFilePath(relativePath);
        if (!File.Exists(fullPath))
            return false;

        try
        {
            File.Delete(fullPath);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void DeleteDirectoryIfEmpty(string relativeDirectory)
    {
        string normalized = RelativePathUtility.NormalizeRelativeDirectoryPath(relativeDirectory);
        if (normalized.Length == 0)
            return;

        string dirPath = ResolveNormalizedDirectoryPath(normalized);
        if (!Directory.Exists(dirPath))
            return;

        try
        {
            if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
                Directory.Delete(dirPath, recursive: false);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
    }

    private string ResolveFilePath(string relativePath)
    {
        string normalized = RelativePathUtility.NormalizeRelativeFilePath(relativePath);
        string combined = Path.GetFullPath(Path.Combine(_rootDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(_rootPrefix, PathComparison))
            throw new ArgumentException($"Resolved path escapes root directory: '{relativePath}'.", nameof(relativePath));

        return combined;
    }

    private string ResolveDirectoryPath(string relativeDirectory)
    {
        string normalized = RelativePathUtility.NormalizeRelativeDirectoryPath(relativeDirectory);
        return ResolveNormalizedDirectoryPath(normalized);
    }

    private string ResolveNormalizedDirectoryPath(string normalized)
    {
        if (normalized.Length == 0)
            return _rootDirectory;

        string combined = Path.GetFullPath(Path.Combine(_rootDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(_rootPrefix, PathComparison))
            throw new ArgumentException($"Resolved directory escapes root directory: '{normalized}'.", nameof(normalized));

        return combined;
    }
}
