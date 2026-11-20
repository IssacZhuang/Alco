// CPU coverage for aligned multi-heap allocation, fragmentation recovery, metadata growth, and steady-state costs.

using System.Diagnostics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;

internal static class HeapPoolRangeTests
{
    internal static void Run(Action<bool, string> check)
    {
        Console.WriteLine("Growable heap ranges:");
        CheckAlignment(check);
        CheckSeparateHeaps(check);
        CheckBoundedSearchFallback(check);
        CheckChurn(check);
        CheckSteadyState(check);
    }

    private static void CheckAlignment(Action<bool, string> check)
    {
        HeapPoolRanges ranges = new(1);
        uint heap = ranges.AddHeap(512);
        bool first = ranges.TryAllocate(3, 1, out HeapPoolRanges.Allocation a);
        bool aligned = ranges.TryAllocate(5, 64, out HeapPoolRanges.Allocation b);
        bool prefix = ranges.TryAllocate(61, 1, out HeapPoolRanges.Allocation c);
        check(first && aligned && prefix && a.Offset == 0 && b.Offset == 64 && c.Offset == 3,
            "per-request alignment preserves the usable prefix instead of charging it to the allocation");
        check(ranges.UsedBytes == 69 && ranges.ReservedBytes == 512 && ranges.AllocationCount == 3,
            "native-sized reservations do not round every request to the largest alignment");
        ranges.Free(a.Token);
        ranges.Free(c.Token);
        ranges.Free(b.Token);
        check(ranges.UsedBytes == 0 && ranges.GetLargestFreeRange() == 512 && ranges.GetHeapAllocationCount(heap) == 0,
            "freeing aligned allocations merges both prefixes and suffixes back into the entire heap");
        bool whole = ranges.TryAllocate(512, 512, out HeapPoolRanges.Allocation all);
        check(whole && all.Offset == 0 && all.HeapIndex == heap, "the completely coalesced heap satisfies a full-sized aligned allocation");
        ranges.Free(all.Token);
        ranges.RemoveHeap(heap);
        check(ranges.ReservedBytes == 0 && ranges.HeapCount == 0 && ranges.GetLargestFreeRange() == 0,
            "removing an empty heap removes its free ranges and reserved capacity");
    }

    private static void CheckSeparateHeaps(Action<bool, string> check)
    {
        HeapPoolRanges ranges = new(1);
        uint firstHeap = ranges.AddHeap(128);
        uint secondHeap = ranges.AddHeap(128);
        bool first = ranges.TryAllocate(128, 16, out HeapPoolRanges.Allocation a);
        bool second = ranges.TryAllocate(128, 16, out HeapPoolRanges.Allocation b);
        check(first && second && a.HeapIndex != b.HeapIndex && a.Offset == 0 && b.Offset == 0,
            "one free-range index allocates across independent heaps without moving existing reservations");
        ranges.Free(a.Token);
        ranges.Free(b.Token);
        check(!ranges.TryAllocate(129, 1, out _) && ranges.GetLargestFreeRange() == 128,
            "coalescing never joins address ranges belonging to different heaps");
        ranges.RemoveHeap(firstHeap);
        uint reused = ranges.AddHeap(64);
        check(reused == firstHeap && ranges.GetHeapSize(secondHeap) == 128,
            "removed heap slots are reused without renumbering another heap");
        ranges.RemoveHeap(reused);
        ranges.RemoveHeap(secondHeap);
    }

    private static void CheckBoundedSearchFallback(Action<bool, string> check)
    {
        HeapPoolRanges ranges = new(1);
        uint fittingHeap = ranges.AddHeap(1050);
        uint[] heaps = new uint[24];
        for (int index = 0; index < heaps.Length; ++index)
            heaps[index] = ranges.AddHeap(1040);
        bool found = ranges.TryAllocate(1050, 1, out HeapPoolRanges.Allocation allocation);
        check(found && allocation.HeapIndex == fittingHeap,
            "exact fallback finds a fitting range behind more candidates than the fast search examines");
        ranges.Free(allocation.Token);
        ranges.RemoveHeap(fittingHeap);
        for (int index = 0; index < heaps.Length; ++index)
            ranges.RemoveHeap(heaps[index]);
        check(ranges.ReservedBytes == 0 && ranges.AllocationCount == 0,
            "heap and node metadata growth preserves tokens and allows complete reclamation");
    }

    private static void CheckChurn(Action<bool, string> check)
    {
        HeapPoolRanges ranges = new(1);
        byte[][] occupied = [new byte[8192], new byte[8192], new byte[4096]];
        for (int index = 0; index < occupied.Length; ++index)
            ranges.AddHeap((ulong)occupied[index].Length);
        HeapPoolRanges.Allocation[] live = new HeapPoolRanges.Allocation[192];
        uint[] sizes = new uint[live.Length];
        Random random = new(9173);
        ulong expectedUsed = 0;
        bool valid = true;
        int failedAllocations = 0;
        for (int iteration = 0; iteration < 30000 && valid; ++iteration)
        {
            int slot = random.Next(live.Length);
            if (sizes[slot] != 0)
            {
                HeapPoolRanges.Allocation allocation = live[slot];
                occupied[allocation.HeapIndex].AsSpan((int)allocation.Offset, (int)sizes[slot]).Clear();
                expectedUsed -= sizes[slot];
                ranges.Free(allocation.Token);
                sizes[slot] = 0;
            }
            else
            {
                uint size = (uint)random.Next(1, 385);
                uint alignment = 1u << random.Next(10);
                if (ranges.TryAllocate(size, alignment, out HeapPoolRanges.Allocation allocation))
                {
                    byte[] storage = occupied[allocation.HeapIndex];
                    valid = allocation.Offset % alignment == 0 && allocation.Offset + size <= (ulong)storage.Length;
                    if (!valid)
                        break;
                    Span<byte> reservation = storage.AsSpan((int)allocation.Offset, (int)size);
                    for (int index = 0; index < reservation.Length; ++index)
                        valid &= reservation[index] == 0;
                    reservation.Fill(1);
                    live[slot] = allocation;
                    sizes[slot] = size;
                    expectedUsed += size;
                }
                else
                {
                    ++failedAllocations;
                    valid = !HasSpace(occupied, (int)size, (int)alignment);
                }
            }
            valid &= ranges.UsedBytes == expectedUsed;
        }
        check(valid && failedAllocations != 0,
            "30,000 mixed-size/alignment operations match a byte occupancy oracle, including exact exhaustion under fragmentation");
        for (int slot = 0; slot < live.Length; ++slot)
        {
            if (sizes[slot] != 0)
                ranges.Free(live[slot].Token);
        }
        check(ranges.UsedBytes == 0 && ranges.AllocationCount == 0 && ranges.GetLargestFreeRange() == 8192,
            "mixed-lifetime churn coalesces fully after every allocation is returned");
        for (uint heap = 0; heap < occupied.Length; ++heap)
            ranges.RemoveHeap(heap);
    }

    private static bool HasSpace(byte[][] heaps, int size, int alignment)
    {
        for (int heap = 0; heap < heaps.Length; ++heap)
        {
            byte[] storage = heaps[heap];
            for (int offset = 0; offset <= storage.Length - size; offset += alignment)
            {
                int end = offset + size;
                int next = offset;
                while (next < end && storage[next] == 0)
                    ++next;
                if (next == end)
                    return true;
            }
        }
        return false;
    }

    private static void CheckSteadyState(Action<bool, string> check)
    {
        HeapPoolRanges ranges = new(64);
        uint heap = ranges.AddHeap(1024 * 1024);
        HeapPoolRanges.Allocation[] live = new HeapPoolRanges.Allocation[64];
        bool valid = true;
        for (int pass = 0; pass < 128; ++pass)
            valid &= Exercise(ranges, live);
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int pass = 0; pass < 2048; ++pass)
            valid &= Exercise(ranges, live);
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(valid && allocated == 0, "warmed aligned allocation and non-LIFO free perform no managed allocations");
        Console.WriteLine($"  heap ranges: 262,144 allocate/free operations in {milliseconds:F2} ms; {allocated} managed bytes");
        check(ranges.GetLargestFreeRange() == 1024 * 1024, "steady-state churn does not accumulate unmergeable free fragments");
        ranges.RemoveHeap(heap);
    }

    private static bool Exercise(HeapPoolRanges ranges, HeapPoolRanges.Allocation[] live)
    {
        bool valid = true;
        for (int index = 0; index < live.Length; ++index)
            valid &= ranges.TryAllocate((ulong)(index * 37 + 17), 1ul << (index % 9), out live[index]);
        for (int index = 0; index < live.Length; index += 2)
            ranges.Free(live[index].Token);
        for (int index = 1; index < live.Length; index += 2)
            ranges.Free(live[index].Token);
        return valid;
    }
}
