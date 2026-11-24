// Configuration and capacity statistics shared by physical heap pools.

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

/// <summary>Configures heap growth, backing capacity, and allocation metadata for a physical heap pool.</summary>
public struct HeapPoolDesc
{
    /// <summary>Initial ordinary heap size in bytes before heap alignment; must be positive. Defaults to 64 MiB.</summary>
    public ulong InitialHeapSize = 64UL * 1024 * 1024;

    /// <summary>Maximum ordinary heap growth target before heap alignment; must cover InitialHeapSize. Larger requests get dedicated heaps.</summary>
    public ulong MaxHeapSize = 256UL * 1024 * 1024;

    /// <summary>Maximum simultaneous backing heap capacity, excluding driver padding and metadata; must be positive. Defaults to unlimited.</summary>
    public ulong Budget = ulong.MaxValue;

    /// <summary>Initial allocation metadata capacity; must be positive and grows independently of heap storage. Defaults to 256.</summary>
    public uint InitialAllocationCapacity = 256;

    /// <summary>Initializes the default heap pool configuration.</summary>
    public HeapPoolDesc()
    {
    }
}

/// <summary>Reports current and peak heap capacity and live allocation usage.</summary>
public struct HeapPoolStatistics
{
    /// <summary>Current backing heap capacity, excluding driver padding and metadata.</summary>
    public ulong ReservedBytes;

    /// <summary>Bytes reserved for live allocations, including per-allocation rounding.</summary>
    public ulong UsedBytes;

    /// <summary>Highest simultaneous backing heap capacity, excluding driver padding and metadata.</summary>
    public ulong PeakReservedBytes;

    /// <summary>Highest simultaneous live allocation usage, including per-allocation rounding.</summary>
    public ulong PeakUsedBytes;

    /// <summary>Number of live allocations.</summary>
    public uint AllocationCount;

    /// <summary>Number of retained backing heaps, including the cached empty heap.</summary>
    public uint HeapCount;

    /// <summary>Largest contiguous free range in any backing heap, before request-specific alignment.</summary>
    public ulong LargestFreeRange;
}
