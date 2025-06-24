using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

/// <summary>
/// A file system implementation that provides read and write access to a directory on disk.
/// This class extends <see cref="DirectoryFileSource"/> to include write capabilities.
/// </summary>
public class DirectoryFileSystem : DirectoryFileSource, IFileSystem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryFileSystem"/> class.
    /// </summary>
    /// <param name="directoryPath">The path to the directory that this file system will manage.</param>
    public DirectoryFileSystem(string directoryPath) : base(directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }



    /// <summary>
    /// Try to write data to a file
    /// </summary>
    /// <param name="path">The path of the file</param>
    /// <param name="data">The data to write</param>
    /// <param name="failureReason">The failure reason</param>
    /// <returns>True if the data is successfully written, false otherwise</returns>
    public bool TryWriteFile(string path, ReadOnlySpan<byte> data, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            var fullPath = Path.Combine(DirectoryPath, path);
            var directory = Path.GetDirectoryName(fullPath);

            // Create directory if it doesn't exist
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write the file
            File.WriteAllBytes(fullPath, data.ToArray());

            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = e.ToString();
            return false;
        }
    }

    /// <summary>
    /// Try to delete a file
    /// </summary>
    /// <param name="path">The path of the file</param>
    /// <param name="failureReason">The failure reason</param>
    /// <returns>True if the file is successfully deleted, false otherwise</returns>
    public bool TryDeleteFile(string path, [NotNullWhen(false)] out string? failureReason)
    {
        try
        {
            var fullPath = Path.Combine(DirectoryPath, path);

            // Check if file exists before trying to delete
            if (!File.Exists(fullPath))
            {
                failureReason = $"File not found: {path}";
                return false;
            }

            // Delete the file
            File.Delete(fullPath);

            failureReason = null;
            return true;
        }
        catch (Exception e)
        {
            failureReason = e.ToString();
            return false;
        }
    }
}