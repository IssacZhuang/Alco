using System.Runtime.InteropServices;

namespace Alco.Graphics;

/// <summary>
/// The [numthreads] dimensions of a compute shader's thread group.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ThreadGroupSize
{
    /// <summary>The 1×1×1 group size used for non-compute programs.</summary>
    public static readonly ThreadGroupSize Default = new ThreadGroupSize(1, 1, 1);

    /// <summary>Creates a thread group size from its per-axis thread counts.</summary>
    /// <param name="x">The thread count along the X axis.</param>
    /// <param name="y">The thread count along the Y axis.</param>
    /// <param name="z">The thread count along the Z axis.</param>
    public ThreadGroupSize(uint x, uint y, uint z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Group size along the X axis.</summary>
    public uint X { get; init; } = 1;
    /// <summary>Group size along the Y axis.</summary>
    public uint Y { get; init; } = 1;
    /// <summary>Group size along the Z axis.</summary>
    public uint Z { get; init; } = 1;

    public override string ToString()
    {
        return $"[Thread Group Size] x: {X}, y: {Y}, z: {Z}";
    }

    /// <summary>
    /// Ceil-divides the workload extent by the group size to get the dispatch count
    /// along the X axis.
    /// </summary>
    /// <param name="x">The workload extent along the X axis.</param>
    /// <param name="dispatchX">The dispatch count along the X axis.</param>
    public void GetDispatchCount(uint x, out uint dispatchX)
    {
        dispatchX = (x + X - 1) / X;
    }

    /// <summary>
    /// Ceil-divides the workload extents by the group size to get the dispatch counts
    /// along the X and Y axes.
    /// </summary>
    /// <param name="x">The workload extent along the X axis.</param>
    /// <param name="y">The workload extent along the Y axis.</param>
    /// <param name="dispatchX">The dispatch count along the X axis.</param>
    /// <param name="dispatchY">The dispatch count along the Y axis.</param>
    public void GetDispatchCount(uint x, uint y, out uint dispatchX, out uint dispatchY)
    {
        dispatchX = (x + X - 1) / X;
        dispatchY = (y + Y - 1) / Y;
    }

    /// <summary>
    /// Ceil-divides the workload extents by the group size to get the dispatch counts
    /// along the X, Y and Z axes.
    /// </summary>
    /// <param name="x">The workload extent along the X axis.</param>
    /// <param name="y">The workload extent along the Y axis.</param>
    /// <param name="z">The workload extent along the Z axis.</param>
    /// <param name="dispatchX">The dispatch count along the X axis.</param>
    /// <param name="dispatchY">The dispatch count along the Y axis.</param>
    /// <param name="dispatchZ">The dispatch count along the Z axis.</param>
    public void GetDispatchCount(uint x, uint y, uint z, out uint dispatchX, out uint dispatchY, out uint dispatchZ)
    {
        dispatchX = (x + X - 1) / X;
        dispatchY = (y + Y - 1) / Y;
        dispatchZ = (z + Z - 1) / Z;
    }

    public static bool operator ==(ThreadGroupSize left, ThreadGroupSize right)
    {
        return left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    }

    public static bool operator !=(ThreadGroupSize left, ThreadGroupSize right)
    {
        return left.X != right.X || left.Y != right.Y || left.Z != right.Z;
    }

    public override bool Equals(object? obj)
    {
        return obj is ThreadGroupSize size && this == size;
    }

    public override int GetHashCode()
    {
        return X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode();
    }
}