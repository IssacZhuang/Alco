using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Alco.IO;

/// <summary>
/// The interface provide file data for <see cref="AssetSystem"/>. 
/// The implementor should be thread safe.
/// <br/>
/// This interface is not recyclable and it will be disposed when the <see cref="AssetSystem"/> is disposed 
/// or on the <see cref="AssetSystem.RemoveAllFileSource"/> and <see cref="AssetSystem.RemoveFileSource"/>
/// </summary>
public interface IFileSource
{
    /// <summary>
    /// The name of this file source
    /// </summary>
    /// <value>The name of this file source</value>
    string Name { get; }

    /// <summary>
    /// The priority of this file source, the higher priority will be override the lower priority
    /// </summary>
    int Priority { get; }
    /// <summary>
    /// All file names in this file source
    /// </summary>
    IEnumerable<string> AllFileNames { get; }

    /// <summary>
    /// Try get data from this file source
    /// </summary>
    /// <param name="path">The path of the file</param>
    /// <param name="data">The data of the file</param>
    /// <param name="failureReason">The failure reason</param>
    /// <returns>True if the data is successfully retrieved, false otherwise</returns>
    bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, [NotNullWhen(false)] out string? failureReason);

    /// <summary>
    /// Try get a stream for reading the file from this file source.
    /// The returned stream supports seeking and should be disposed by the caller.
    /// </summary>
    /// <param name="path">The path of the file</param>
    /// <param name="stream">The stream for reading the file content</param>
    /// <param name="failureReason">The failure reason</param>
    /// <returns>True if the stream is successfully created, false otherwise</returns>
    bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason);
}


