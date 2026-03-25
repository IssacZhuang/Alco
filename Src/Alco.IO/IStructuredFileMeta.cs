using Alco;

namespace Alco.IO;

/// <summary>
/// Marker interface for structured file metadata types.
/// Provides the file type's magic number as a static abstract member,
/// enabling compile-time safety in file I/O operations.
/// </summary>
public interface IStructuredFileMeta : ISerializable
{
    /// <summary>
    /// Gets the 4-byte magic number that identifies this file type.
    /// </summary>
    static abstract ReadOnlySpan<byte> Magic { get; }
}
