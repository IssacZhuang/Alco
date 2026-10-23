using Alco;

namespace Alco.IO;

/// <summary>
/// Marker interface for package metadata types. Provides the file type's 4-byte magic number
/// as a static abstract member, enabling compile-time-typed package readers and builders.
/// </summary>
/// <remarks>
/// Concrete metas must declare this interface directly; inheriting it only through a base type makes generic <c>TMeta.Magic</c> resolve to the base slot.
/// </remarks>
public interface IPackageMeta : ISerializable
{
    /// <summary>
    /// Gets the 4-byte magic number that identifies this package file type.
    /// </summary>
    static abstract ReadOnlySpan<byte> Magic { get; }
}
