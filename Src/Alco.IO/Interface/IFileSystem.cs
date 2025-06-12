
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

/// <summary>
/// The interface for read and write file
/// <br/>
/// The implementor should be thread safe.
/// </summary>
public interface IFileSystem : IFileSource
{
    /// <summary>
    /// Try to write data to a file
    /// </summary>
    /// <param name="path">The path of the file</param>
    /// <param name="data">The data to write</param>
    /// <param name="failureReason">The failure reason</param>
    /// <returns>True if the data is successfully written, false otherwise</returns>
    bool TryWriteFile(string path, ReadOnlySpan<byte> data, [NotNullWhen(false)] out string? failureReason);
}