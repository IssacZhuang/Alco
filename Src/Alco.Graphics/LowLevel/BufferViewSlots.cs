// Stable buffer-view slots shared by backend-native address tables.

using System.Diagnostics;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

internal sealed unsafe class BufferViewSlots(ulong* addresses)
{
    internal const uint Capacity = 65_536;
    private readonly Lock Sync = new();
    private readonly uint[] FreeSlots = new uint[Capacity - 1];
    private uint FreeCount;
    private uint NextIndex = 1;

    internal uint Allocate(ulong address)
    {
        lock (Sync)
        {
            uint index;
            if (FreeCount != 0)
                index = FreeSlots[--FreeCount];
            else if (NextIndex < Capacity)
                index = NextIndex++;
            else
                return 0;
            addresses[index] = address;
            return index;
        }
    }

    internal void Free(uint index)
    {
        Debug.Assert(index != 0 && index < Capacity);
        lock (Sync)
        {
            addresses[index] = 0;
            FreeSlots[FreeCount++] = index;
        }
    }
}
