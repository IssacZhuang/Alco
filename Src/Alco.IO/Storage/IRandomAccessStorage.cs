using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

/// <summary>
/// Thread-safe random-access file handle used by streaming chunk storage.
/// </summary>
public interface IRandomAccessHandle : IDisposable
{
    /// <summary>
    /// Gets the current byte length of the underlying file.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Reads bytes starting at <paramref name="offset"/> until <paramref name="destination"/> is filled or end-of-file is reached.
    /// </summary>
    /// <param name="offset">The non-negative byte offset to read from.</param>
    /// <param name="destination">The buffer receiving read bytes.</param>
    /// <returns>The total number of bytes read.</returns>
    int ReadAt(long offset, Span<byte> destination);

    /// <summary>
    /// Writes bytes starting at <paramref name="offset"/>, extending the file if necessary.
    /// </summary>
    /// <param name="offset">The non-negative byte offset to write to.</param>
    /// <param name="source">The bytes to write.</param>
    void WriteAt(long offset, ReadOnlySpan<byte> source);

    /// <summary>
    /// Flushes buffered file writes and metadata to durable storage.
    /// </summary>
    void FlushToDisk();
}

/// <summary>
/// Abstraction over directory-backed or in-memory random-access storage for streaming world saves.
/// </summary>
public interface IRandomAccessStorage
{
    /// <summary>
    /// Checks whether a file exists at <paramref name="relativePath"/>.
    /// </summary>
    /// <param name="relativePath">The storage-relative file path.</param>
    /// <returns><see langword="true"/> if the file exists; otherwise, <see langword="false"/>.</returns>
    bool FileExists(string relativePath);

    /// <summary>
    /// Attempts to open an existing file for concurrent random-access reading.
    /// </summary>
    /// <param name="relativePath">The storage-relative file path.</param>
    /// <param name="handle">The opened read handle when successful.</param>
    /// <returns><see langword="true"/> if the file existed and was opened; otherwise, <see langword="false"/>.</returns>
    bool TryOpenRead(string relativePath, [NotNullWhen(true)] out IRandomAccessHandle? handle);

    /// <summary>
    /// Opens an existing file or creates a new file for random-access read/write operations.
    /// </summary>
    /// <param name="relativePath">The storage-relative file path.</param>
    /// <returns>An open read/write handle positioned independently via offsets.</returns>
    IRandomAccessHandle OpenAppendOrCreate(string relativePath);

    /// <summary>
    /// Atomically writes or replaces the file at <paramref name="relativePath"/>.
    /// </summary>
    /// <param name="relativePath">The storage-relative file path.</param>
    /// <param name="data">The complete file contents.</param>
    /// <param name="flushToDisk">Whether to flush data to durable storage before replacing the target.</param>
    void WriteAtomic(string relativePath, ReadOnlySpan<byte> data, bool flushToDisk = true);

    /// <summary>
    /// Attempts to read the entire contents of an existing file.
    /// </summary>
    /// <param name="relativePath">The storage-relative file path.</param>
    /// <param name="data">The file bytes when successful.</param>
    /// <returns><see langword="true"/> if the file existed and was read; otherwise, <see langword="false"/>.</returns>
    bool TryReadAllBytes(string relativePath, [NotNullWhen(true)] out byte[]? data);

    /// <summary>
    /// Enumerates files in <paramref name="relativeDirectory"/> matching <paramref name="searchPattern"/>.
    /// </summary>
    /// <param name="relativeDirectory">The storage-relative directory path.</param>
    /// <param name="searchPattern">The simple file name wildcard pattern (e.g. <c>"*.snap"</c>).</param>
    /// <param name="recursive">Whether to include subdirectories recursively.</param>
    /// <returns>Sorted normalized storage-relative file paths.</returns>
    IReadOnlyList<string> EnumerateFiles(string relativeDirectory, string searchPattern, bool recursive = false);

    /// <summary>
    /// Enumerates immediate child directories under <paramref name="relativeDirectory"/>.
    /// </summary>
    /// <param name="relativeDirectory">The storage-relative directory path.</param>
    /// <returns>Sorted normalized storage-relative directory paths.</returns>
    IReadOnlyList<string> EnumerateDirectories(string relativeDirectory);

    /// <summary>
    /// Deletes the file at <paramref name="relativePath"/> if it exists.
    /// </summary>
    /// <param name="relativePath">The storage-relative file path.</param>
    /// <returns><see langword="true"/> if the file existed and was deleted; otherwise, <see langword="false"/>.</returns>
    bool DeleteFile(string relativePath);

    /// <summary>
    /// Deletes <paramref name="relativeDirectory"/> if it exists and contains no files or subdirectories.
    /// </summary>
    /// <param name="relativeDirectory">The storage-relative directory path.</param>
    void DeleteDirectoryIfEmpty(string relativeDirectory);
}
