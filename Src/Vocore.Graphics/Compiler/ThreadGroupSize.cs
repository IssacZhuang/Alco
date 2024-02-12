using System.Runtime.InteropServices;

namespace Vocore.Graphics;

[StructLayout(LayoutKind.Sequential)]
public struct ThreadGroupSize
{
    public static readonly ThreadGroupSize Default = new ThreadGroupSize(1, 1, 1);
    public ThreadGroupSize(uint x, uint y, uint z)
    {
        X = x;
        Y = y;
        Z = z;
    }
    public uint X { get; init; } = 1;
    public uint Y { get; init; } = 1;
    public uint Z { get; init; } = 1;

    public override string ToString()
    {
        return $"[Thread Group Size] x: {X}, y: {Y}, z: {Z}";
    }

    // default y=1 z = 1
    public void GetDispatchCount(uint x, out uint dispatchX)
    {
        dispatchX = (x + X - 1) / X;
    }

    //default z = 1
    public void GetDispatchCount(uint x, uint y, out uint dispatchX, out uint dispatchY)
    {
        dispatchX = (x + X - 1) / X;
        dispatchY = (y + Y - 1) / Y;
    }

    public void GetDispatchCount(uint x, uint y, uint z, out uint dispatchX, out uint dispatchY, out uint dispatchZ)
    {
        dispatchX = (x + X - 1) / X;
        dispatchY = (y + Y - 1) / Y;
        dispatchZ = (z + Z - 1) / Z;
    }

    //override ==
    public static bool operator ==(ThreadGroupSize left, ThreadGroupSize right)
    {
        return left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    }

    //override !=
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