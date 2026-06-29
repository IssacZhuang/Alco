using Alco;

namespace Alco.IO;

/// <summary>
/// Marker interface for package metadata types. Provides the file type's 4-byte magic number
/// as a static abstract member, enabling compile-time-typed package readers and builders.
/// </summary>
/// <remarks>
/// <para>
/// A concrete meta type declares this interface <b>directly</b> and supplies its own
/// <see cref="Magic"/> slot. It must NOT be reached only via inheritance: a derived type that
/// re-declares <see cref="Magic"/> while a base already implements this interface only *hides*
/// the base slot (CS0108), and generic dispatch <c>TMeta.Magic</c> would resolve to the base
/// value. The intended pattern is a shared abstract base (e.g. <see cref="PackageMetaBase"/>)
/// that implements <see cref="ISerializable"/> but <b>not</b> this interface, with each concrete
/// meta (<c>PackageMeta</c>, save metas) declaring <see cref="IPackageMeta"/> itself.
/// </para>
/// </remarks>
public interface IPackageMeta : ISerializable
{
    /// <summary>
    /// Gets the 4-byte magic number that identifies this package file type.
    /// </summary>
    static abstract ReadOnlySpan<byte> Magic { get; }
}
