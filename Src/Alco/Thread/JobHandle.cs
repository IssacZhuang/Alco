using System;

namespace Alco;

/// <summary>
/// Lightweight handle identifying a scheduled job for dependency chaining and completion checks.
/// </summary>
public readonly struct JobHandle : IEquatable<JobHandle>
{
    internal readonly int Index;
    internal readonly int Generation;

    internal JobHandle(int index, int generation)
    {
        Index = index;
        Generation = generation;
    }

    /// <summary>Gets whether this handle refers to a scheduled job rather than <see langword="default"/>.</summary>
    public bool IsValid => Generation != 0;

    /// <inheritdoc />
    public bool Equals(JobHandle other) => Index == other.Index && Generation == other.Generation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is JobHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Index, Generation);

    /// <summary>Determines whether two handles refer to the same scheduled job instance.</summary>
    public static bool operator ==(JobHandle left, JobHandle right) => left.Equals(right);

    /// <summary>Determines whether two handles refer to different scheduled job instances.</summary>
    public static bool operator !=(JobHandle left, JobHandle right) => !left.Equals(right);
}
