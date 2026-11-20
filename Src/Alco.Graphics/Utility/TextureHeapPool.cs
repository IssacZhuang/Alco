// Budgeted physical texture heaps with native placement requirements and explicit lifetimes.

using System.Diagnostics;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Owns heaps for placed textures. Externally synchronize operations and keep the device alive until destruction.</summary>
public sealed class TextureHeapPool
{
    private struct HeapEntry
    {
        internal GPUTextureHeap Heap;
        internal bool Dedicated;
    }

    private readonly GPUDevice Device;
    private readonly ulong HeapAlignment;
    private readonly ulong MaxHeapSize;
    private readonly ulong Budget;
    private readonly HeapPoolRanges Ranges;
    private HeapEntry[] Heaps = new HeapEntry[4];
    private ulong NextHeapSize;
    private uint EmptyHeap = HeapPoolRanges.Invalid;
    private bool Destroyed;

    /// <summary>Creates an empty pool; backing heaps are allocated on demand.</summary>
    /// <param name="device">The device that owns the backing heaps and textures.</param>
    /// <param name="desc">Positive heap sizes, budget, and metadata capacity; the maximum heap size must cover the initial size.</param>
    public TextureHeapPool(GPUDevice device, in HeapPoolDesc desc)
    {
        Debug.Assert(desc.InitialHeapSize != 0 && desc.MaxHeapSize >= desc.InitialHeapSize && desc.Budget != 0);
        Debug.Assert(desc.InitialAllocationCapacity != 0);
        Device = device;
        HeapAlignment = device.Caps.TextureHeapAlignment;
        Debug.Assert(HeapAlignment != 0 && (HeapAlignment & (HeapAlignment - 1)) == 0);
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

    /// <summary>Allocates an uninitialized texture using its native size and alignment. Oversized textures reserve their entire dedicated heap.</summary>
    /// <param name="desc">The texture description.</param>
    /// <returns>The placed texture, or an empty result for unsupported requirements or exhausted budget.</returns>
    /// <exception cref="GPUException">Heap or texture creation failed; any reservation and newly created empty heap are released.</exception>
    public PlacedTexture Allocate(in TextureDesc desc)
    {
        Debug.Assert(!Destroyed);
        SizeAlign requirements = Device.GetTextureSizeAlign(desc);
        if (requirements.Size == 0)
            return default;

        uint newHeapIndex = HeapPoolRanges.Invalid;
        ulong previousNextHeapSize = NextHeapSize;
        if (!Ranges.TryAllocate(requirements.Size, requirements.Align, out HeapPoolRanges.Allocation allocation))
        {
            if (!Grow(requirements.Size, out newHeapIndex))
                return default;
            ulong size = Heaps[newHeapIndex].Dedicated ? Ranges.GetHeapSize(newHeapIndex) : requirements.Size;
            bool allocated = Ranges.TryAllocate(size, requirements.Align, out allocation);
            Debug.Assert(allocated && allocation.HeapIndex == newHeapIndex);
        }
        try
        {
            GPUTexture texture = Device.CreateTexture(desc, Heaps[allocation.HeapIndex].Heap, allocation.Offset);
            if (EmptyHeap == allocation.HeapIndex)
                EmptyHeap = HeapPoolRanges.Invalid;
            return new PlacedTexture { Texture = texture, Token = allocation.Token };
        }
        catch (GPUException)
        {
            Ranges.Free(allocation.Token);
            if (newHeapIndex != HeapPoolRanges.Invalid)
            {
                DestroyHeap(newHeapIndex);
                NextHeapSize = previousNextHeapSize;
            }
            throw;
        }
    }

    /// <summary>Destroys a texture after its views are destroyed and its recorded and executing GPU uses end; an empty allocation is ignored.</summary>
    /// <param name="texture">An allocation from this pool that has not already been returned, reset to default on return.</param>
    public void Free(ref PlacedTexture texture)
    {
        Debug.Assert(!Destroyed);
        if (texture.Texture == null)
            return;

        uint heapIndex = Ranges.GetHeapIndex(texture.Token);
        texture.Texture.Destroy();
        Ranges.Free(texture.Token);
        texture = default;
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
        if (byteCount > (Budget & ~(HeapAlignment - 1)))
            return false;

        ulong available = (Budget - Ranges.ReservedBytes) & ~(HeapAlignment - 1);
        if (byteCount > available && EmptyHeap != HeapPoolRanges.Invalid)
        {
            DestroyHeap(EmptyHeap);
            available = (Budget - Ranges.ReservedBytes) & ~(HeapAlignment - 1);
        }
        if (byteCount > available)
            return false;

        bool dedicated = byteCount > MaxHeapSize;
        ulong targetSize = dedicated ? byteCount : NextHeapSize;
        while (targetSize < byteCount)
            targetSize = targetSize <= MaxHeapSize / 2 ? targetSize * 2 : MaxHeapSize;
        ulong heapSize = (Math.Min(targetSize, available) + HeapAlignment - 1) & ~(HeapAlignment - 1);
        GPUTextureHeap heap = Device.CreateTextureHeap(heapSize);
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
