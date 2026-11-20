// Budgeted physical GPU heaps with aligned suballocations and explicit lifetimes.

using System.Diagnostics;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Owns GPU heaps for aligned suballocations. Externally synchronize operations and keep the device alive until destruction.</summary>
public sealed unsafe class GPUHeapPool
{
    private const ulong Alignment = 16;

    private struct HeapEntry
    {
        internal GPUHeap Heap;
        internal bool Dedicated;
    }

    private readonly GPUDevice Device;
    private readonly MemoryType Memory;
    private readonly ulong MaxHeapSize;
    private readonly ulong Budget;
    private readonly HeapPoolRanges Ranges;
    private HeapEntry[] Heaps = new HeapEntry[4];
    private ulong NextHeapSize;
    private uint EmptyHeap = HeapPoolRanges.Invalid;
    private bool Destroyed;

    /// <summary>Creates an empty pool; backing heaps are allocated on demand.</summary>
    /// <param name="device">The device that owns the backing heaps.</param>
    /// <param name="desc">Positive heap sizes, budget, and metadata capacity; the maximum heap size must cover the initial size.</param>
    /// <param name="memory">The memory type of every backing heap.</param>
    public GPUHeapPool(GPUDevice device, in HeapPoolDesc desc, MemoryType memory = MemoryType.CpuVisible)
    {
        Debug.Assert(desc.InitialHeapSize != 0 && desc.MaxHeapSize >= desc.InitialHeapSize && desc.Budget != 0);
        Debug.Assert(desc.InitialAllocationCapacity != 0);
        Device = device;
        Memory = memory;
        MaxHeapSize = desc.MaxHeapSize;
        Budget = desc.Budget;
        NextHeapSize = desc.InitialHeapSize;
        Ranges = new HeapPoolRanges(desc.InitialAllocationCapacity);
    }

    /// <summary>Destroys the backing heaps after every allocation has been returned. Does not wait for the GPU.</summary>
    public void Destroy()
    {
        Debug.Assert(!Destroyed && Ranges.AllocationCount == 0);
        for (uint heapIndex = 0; heapIndex < Heaps.Length; ++heapIndex)
        {
            if (Heaps[heapIndex].Heap.Owner != null)
                DestroyHeap(heapIndex);
        }
        Heaps = [];
        Destroyed = true;
    }

    /// <summary>Allocates bytes at a 16-byte-aligned buffer offset, reserving a multiple of 16 bytes.</summary>
    /// <param name="byteCount">The positive requested size in bytes.</param>
    /// <returns>The exact requested range, or an empty allocation when the budget cannot accommodate it; GPU-only memory has no CPU mapping.</returns>
    /// <exception cref="GPUException">A backing heap could not be created.</exception>
    public HeapAllocation<byte> Allocate(ulong byteCount)
    {
        Debug.Assert(!Destroyed && byteCount != 0);
        if (byteCount > ulong.MaxValue - (Alignment - 1))
            return new HeapAllocation<byte>();

        ulong size = (byteCount + Alignment - 1) & ~(Alignment - 1);
        if (!Ranges.TryAllocate(size, Alignment, out HeapPoolRanges.Allocation allocation))
        {
            if (!Grow(byteCount, out uint heapIndex))
                return new HeapAllocation<byte>();
            bool allocated = Ranges.TryAllocate(size, Alignment, out allocation);
            Debug.Assert(allocated && allocation.HeapIndex == heapIndex);
        }
        if (EmptyHeap == allocation.HeapIndex)
            EmptyHeap = HeapPoolRanges.Invalid;

        GPUAllocation<byte> storage = Heaps[allocation.HeapIndex].Heap.Range;
        return new HeapAllocation<byte>
        {
            Range = new GPUAllocation<byte>
            {
                Cpu = storage.Cpu == null ? null : storage.Cpu + allocation.Offset,
                Buffer = storage.Buffer,
                Offset = storage.Offset + allocation.Offset,
                SizeBytes = byteCount,
            },
            Token = allocation.Token,
        };
    }

    /// <summary>Allocates contiguous elements at a 16-byte-aligned buffer offset, reserving a multiple of 16 bytes.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="elementCount">The positive number of elements to allocate.</param>
    /// <returns>The exact requested range, or an empty allocation when the budget cannot accommodate it; GPU-only memory has no CPU mapping.</returns>
    /// <exception cref="GPUException">A backing heap could not be created.</exception>
    public HeapAllocation<T> Allocate<T>(ulong elementCount = 1) where T : unmanaged
    {
        Debug.Assert(!Destroyed && elementCount != 0);
        if (elementCount > ulong.MaxValue / (ulong)sizeof(T))
            return new HeapAllocation<T>();

        HeapAllocation<byte> allocation = Allocate(elementCount * (ulong)sizeof(T));
        return new HeapAllocation<T>
        {
            Range = new GPUAllocation<T>
            {
                Cpu = (T*)allocation.Range.Cpu,
                Buffer = allocation.Range.Buffer,
                Offset = allocation.Range.Offset,
                SizeBytes = allocation.Range.SizeBytes,
            },
            Token = allocation.Token,
        };
    }

    /// <summary>Returns an allocation after its recorded and executing GPU uses end; an empty allocation is ignored.</summary>
    /// <typeparam name="T">The allocation's unmanaged element type.</typeparam>
    /// <param name="allocation">An allocation from this pool that has not already been returned.</param>
    public void Free<T>(HeapAllocation<T> allocation) where T : unmanaged
    {
        Debug.Assert(!Destroyed);
        if (allocation.Range.SizeBytes == 0)
            return;

        uint heapIndex = Ranges.GetHeapIndex(allocation.Token);
        Ranges.Free(allocation.Token);
        ReleaseIfEmpty(heapIndex);
    }

    /// <summary>Destroys every empty backing heap without changing live allocations.</summary>
    public void Trim()
    {
        Debug.Assert(!Destroyed);
        if (EmptyHeap != HeapPoolRanges.Invalid)
            DestroyHeap(EmptyHeap);
    }

    /// <summary>Returns current and peak backing capacity and allocation usage.</summary>
    /// <returns>A snapshot of the pool's statistics.</returns>
    public HeapPoolStatistics GetStatistics()
    {
        return new HeapPoolStatistics
        {
            ReservedBytes = Ranges.ReservedBytes,
            UsedBytes = Ranges.UsedBytes,
            PeakReservedBytes = Ranges.PeakReservedBytes,
            PeakUsedBytes = Ranges.PeakUsedBytes,
            AllocationCount = Ranges.AllocationCount,
            HeapCount = Ranges.HeapCount,
            LargestFreeRange = Ranges.GetLargestFreeRange(),
        };
    }

    private bool Grow(ulong byteCount, out uint heapIndex)
    {
        heapIndex = HeapPoolRanges.Invalid;
        if (byteCount > (Budget & ~(Alignment - 1)))
            return false;

        ulong available = (Budget - Ranges.ReservedBytes) & ~(Alignment - 1);
        if (byteCount > available && EmptyHeap != HeapPoolRanges.Invalid)
        {
            DestroyHeap(EmptyHeap);
            available = (Budget - Ranges.ReservedBytes) & ~(Alignment - 1);
        }
        if (byteCount > available)
            return false;

        bool dedicated = byteCount > MaxHeapSize;
        ulong targetSize = dedicated ? byteCount : NextHeapSize;
        while (targetSize < byteCount)
            targetSize = targetSize <= MaxHeapSize / 2 ? targetSize * 2 : MaxHeapSize;
        ulong heapSize = (Math.Min(targetSize, available) + Alignment - 1) & ~(Alignment - 1);
        GPUHeap heap = Device.CreateGPUHeap(heapSize, Memory);
        heapIndex = Ranges.AddHeap(heapSize);
        if (heapIndex >= Heaps.Length)
            Array.Resize(ref Heaps, Math.Max(Heaps.Length * 2, (int)heapIndex + 1));
        Heaps[heapIndex] = new HeapEntry { Heap = heap, Dedicated = dedicated };
        if (!dedicated)
            NextHeapSize = targetSize <= MaxHeapSize / 2 ? targetSize * 2 : MaxHeapSize;
        return true;
    }

    private void ReleaseIfEmpty(uint heapIndex)
    {
        if (Ranges.GetHeapAllocationCount(heapIndex) != 0)
            return;
        if (Heaps[heapIndex].Dedicated)
        {
            DestroyHeap(heapIndex);
            return;
        }
        if (EmptyHeap != HeapPoolRanges.Invalid)
        {
            if (Ranges.GetHeapSize(heapIndex) <= Ranges.GetHeapSize(EmptyHeap))
            {
                DestroyHeap(heapIndex);
                return;
            }
            DestroyHeap(EmptyHeap);
        }
        EmptyHeap = heapIndex;
    }

    private void DestroyHeap(uint heapIndex)
    {
        Debug.Assert(Ranges.GetHeapAllocationCount(heapIndex) == 0);
        Heaps[heapIndex].Heap.Destroy();
        Heaps[heapIndex] = default;
        Ranges.RemoveHeap(heapIndex);
        if (EmptyHeap == heapIndex)
            EmptyHeap = HeapPoolRanges.Invalid;
    }
}
