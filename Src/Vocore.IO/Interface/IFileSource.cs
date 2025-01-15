using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Vocore.IO;

/// <summary>
/// The interface provide file data for <see cref="AssetSystem"/>. 
/// The implementor should be thread safe.
/// <br/>
/// This interface is not recyclable and it will be disposed when the <see cref="AssetSystem"/> is disposed 
/// or on the <see cref="AssetSystem.RemoveAllFileSource"/> and <see cref="AssetSystem.RemoveFileSource"/>
/// </summary>
public interface IFileSource : IDisposable
{
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
    bool TryGetData(string path, [NotNullWhen(true)] out ReadOnlySpan<byte> data);
}


