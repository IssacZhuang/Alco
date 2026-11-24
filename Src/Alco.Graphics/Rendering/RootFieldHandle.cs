namespace Alco.Graphics;

/// <summary>A strongly-typed handle to a field within a shader's <see cref="RootLayout"/>.</summary>
/// <typeparam name="T">The unmanaged C# struct or scalar matching the reflected shader field layout.</typeparam>
public readonly struct RootField<T> : IEquatable<RootField<T>> where T : unmanaged
{
    internal RootField(uint offset, uint sizeBytes)
    {
        Offset = offset;
        SizeBytes = sizeBytes;
    }

    /// <summary>The byte offset within the shader's root struct.</summary>
    public uint Offset { get; }

    /// <summary>The field size in bytes.</summary>
    public uint SizeBytes { get; }

    /// <summary>Whether this handle refers to a resolved field (<see cref="SizeBytes"/> != 0).</summary>
    public bool IsValid => SizeBytes != 0;

    /// <inheritdoc />
    public bool Equals(RootField<T> other) => Offset == other.Offset && SizeBytes == other.SizeBytes;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RootField<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Offset, SizeBytes);

    /// <summary>Tests whether two field handles reference the same offset and size.</summary>
    public static bool operator ==(RootField<T> left, RootField<T> right) => left.Equals(right);

    /// <summary>Tests whether two field handles differ.</summary>
    public static bool operator !=(RootField<T> left, RootField<T> right) => !left.Equals(right);
}
