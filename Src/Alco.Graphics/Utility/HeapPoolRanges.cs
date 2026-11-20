// Shared aligned range allocation across independent heaps, with two-level free bins and adjacent coalescing.

using System.Diagnostics;
using System.Numerics;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

internal sealed class HeapPoolRanges
{
    internal const uint Invalid = uint.MaxValue;
    private const int BinsPerLeaf = 32;
    private const int TopBinCount = 64;
    private const int SearchLimit = 8;

    internal struct Allocation
    {
        public uint HeapIndex;
        public uint Token;
        public ulong Offset;
    }

    private struct Node
    {
        internal ulong Offset;
        internal ulong Size;
        internal uint HeapIndex;
        internal uint Previous;
        internal uint Next;
        internal uint BinPrevious;
        internal uint BinNext;
        internal bool Used;
    }

    private struct Heap
    {
        internal ulong Size;
        internal uint First;
        internal uint AllocationCount;
    }

    private Node[] Nodes;
    private Heap[] Heaps = new Heap[4];
    private readonly uint[] Bins = new uint[TopBinCount * BinsPerLeaf];
    private readonly uint[] Leaves = new uint[TopBinCount];
    private ulong Tops;
    private uint FreeHead = Invalid;
    private uint FreeCount;
    private uint HeapLength;

    internal uint HeapCount { get; private set; }
    internal uint AllocationCount { get; private set; }
    internal ulong ReservedBytes { get; private set; }
    internal ulong UsedBytes { get; private set; }
    internal ulong PeakReservedBytes { get; private set; }
    internal ulong PeakUsedBytes { get; private set; }

    internal HeapPoolRanges(uint initialAllocationCapacity)
    {
        Debug.Assert(initialAllocationCapacity != 0);
        Nodes = new Node[checked((int)initialAllocationCapacity * 2 + 3)];
        Array.Fill(Bins, Invalid);
        AddFreeNodes(0);
    }

    internal uint AddHeap(ulong size)
    {
        Debug.Assert(size != 0);
        uint heapIndex = 0;
        while (heapIndex < HeapLength && Heaps[heapIndex].Size != 0)
            ++heapIndex;
        if (heapIndex == Heaps.Length)
            Array.Resize(ref Heaps, checked(Heaps.Length * 2));
        if (heapIndex == HeapLength)
            ++HeapLength;
        EnsureFreeNodes(1);
        uint nodeIndex = AcquireNode();
        Nodes[nodeIndex] = new Node
        {
            Size = size,
            HeapIndex = heapIndex,
            Previous = Invalid,
            Next = Invalid,
        };
        Heaps[heapIndex] = new Heap { Size = size, First = nodeIndex };
        InsertFree(nodeIndex);
        ++HeapCount;
        ReservedBytes += size;
        PeakReservedBytes = Math.Max(PeakReservedBytes, ReservedBytes);
        return heapIndex;
    }

    internal void RemoveHeap(uint heapIndex)
    {
        Debug.Assert(heapIndex < HeapLength && Heaps[heapIndex].Size != 0 && Heaps[heapIndex].AllocationCount == 0);
        Heap heap = Heaps[heapIndex];
        Debug.Assert(!Nodes[heap.First].Used && Nodes[heap.First].Size == heap.Size);
        RemoveFree(heap.First);
        ReleaseNode(heap.First);
        Heaps[heapIndex] = default;
        --HeapCount;
        ReservedBytes -= heap.Size;
    }

    internal bool TryAllocate(ulong size, ulong alignment, out Allocation allocation)
    {
        Debug.Assert(size != 0 && alignment != 0 && (alignment & (alignment - 1)) == 0);
        allocation = default;
        int firstBin = SizeToBin(size);
        uint nodeIndex = FindFit(Bins[firstBin], size, alignment, SearchLimit);
        if (nodeIndex == Invalid && size <= ulong.MaxValue - (alignment - 1))
        {
            ulong guaranteedSize = size + alignment - 1;
            int bin = FindBin(SizeToBin(guaranteedSize));
            if (bin >= 0)
            {
                nodeIndex = FindFit(Bins[bin], size, alignment, SearchLimit);
                if (nodeIndex == Invalid)
                {
                    bin = FindBin(bin + 1);
                    if (bin >= 0)
                        nodeIndex = Bins[bin];
                }
            }
        }
        // Alignment and the bounded search may leave fitting ranges behind; exhaust them before requesting more backing storage.
        if (nodeIndex == Invalid)
        {
            for (int bin = FindBin(firstBin); bin >= 0; bin = FindBin(bin + 1))
            {
                nodeIndex = FindFit(Bins[bin], size, alignment, int.MaxValue);
                if (nodeIndex != Invalid)
                    break;
            }
        }
        if (nodeIndex == Invalid)
            return false;

        Node original = Nodes[nodeIndex];
        ulong padding = Padding(original.Offset, alignment);
        Debug.Assert(padding <= original.Size && size <= original.Size - padding);
        ulong remaining = original.Size - padding - size;
        EnsureFreeNodes((padding != 0 ? 1u : 0u) + (remaining != 0 ? 1u : 0u));
        RemoveFree(nodeIndex);
        uint previous = original.Previous;
        uint next = original.Next;
        if (padding != 0)
        {
            uint prefix = AcquireNode();
            Nodes[prefix] = new Node
            {
                Offset = original.Offset,
                Size = padding,
                HeapIndex = original.HeapIndex,
                Previous = previous,
                Next = nodeIndex,
            };
            if (previous == Invalid)
                Heaps[original.HeapIndex].First = prefix;
            else
                Nodes[previous].Next = prefix;
            previous = prefix;
            InsertFree(prefix);
        }
        if (remaining != 0)
        {
            uint suffix = AcquireNode();
            Nodes[suffix] = new Node
            {
                Offset = original.Offset + padding + size,
                Size = remaining,
                HeapIndex = original.HeapIndex,
                Previous = nodeIndex,
                Next = next,
            };
            if (next != Invalid)
                Nodes[next].Previous = suffix;
            next = suffix;
            InsertFree(suffix);
        }
        Nodes[nodeIndex] = new Node
        {
            Offset = original.Offset + padding,
            Size = size,
            HeapIndex = original.HeapIndex,
            Previous = previous,
            Next = next,
            Used = true,
        };
        ++Heaps[original.HeapIndex].AllocationCount;
        ++AllocationCount;
        UsedBytes += size;
        PeakUsedBytes = Math.Max(PeakUsedBytes, UsedBytes);
        allocation = new Allocation { HeapIndex = original.HeapIndex, Token = nodeIndex, Offset = original.Offset + padding };
        return true;
    }

    internal void Free(uint token)
    {
        Debug.Assert(token < Nodes.Length && Nodes[token].Used);
        ref Node node = ref Nodes[token];
        --Heaps[node.HeapIndex].AllocationCount;
        --AllocationCount;
        UsedBytes -= node.Size;
        if (node.Previous != Invalid && !Nodes[node.Previous].Used)
        {
            uint previous = node.Previous;
            RemoveFree(previous);
            node.Offset = Nodes[previous].Offset;
            node.Size += Nodes[previous].Size;
            node.Previous = Nodes[previous].Previous;
            ReleaseNode(previous);
        }
        if (node.Next != Invalid && !Nodes[node.Next].Used)
        {
            uint next = node.Next;
            RemoveFree(next);
            node.Size += Nodes[next].Size;
            node.Next = Nodes[next].Next;
            ReleaseNode(next);
        }
        if (node.Previous == Invalid)
            Heaps[node.HeapIndex].First = token;
        else
            Nodes[node.Previous].Next = token;
        if (node.Next != Invalid)
            Nodes[node.Next].Previous = token;
        InsertFree(token);
    }

    internal uint GetHeapIndex(uint token)
    {
        Debug.Assert(token < Nodes.Length && Nodes[token].Used);
        return Nodes[token].HeapIndex;
    }

    internal uint GetHeapAllocationCount(uint heapIndex) => Heaps[heapIndex].AllocationCount;

    internal ulong GetHeapSize(uint heapIndex) => Heaps[heapIndex].Size;

    internal ulong GetLargestFreeRange()
    {
        if (Tops == 0)
            return 0;
        int top = BitOperations.Log2(Tops);
        int bin = top * BinsPerLeaf + BitOperations.Log2(Leaves[top]);
        ulong largest = 0;
        for (uint node = Bins[bin]; node != Invalid; node = Nodes[node].BinNext)
            largest = Math.Max(largest, Nodes[node].Size);
        return largest;
    }

    private uint FindFit(uint first, ulong size, ulong alignment, int limit)
    {
        for (uint node = first; node != Invalid && limit-- != 0; node = Nodes[node].BinNext)
        {
            ulong padding = Padding(Nodes[node].Offset, alignment);
            if (padding <= Nodes[node].Size && size <= Nodes[node].Size - padding)
                return node;
        }
        return Invalid;
    }

    private int FindBin(int first)
    {
        if (first >= Bins.Length)
            return -1;
        int top = first / BinsPerLeaf;
        uint leaves = Leaves[top] & (uint.MaxValue << (first % BinsPerLeaf));
        if (leaves != 0)
            return top * BinsPerLeaf + BitOperations.TrailingZeroCount(leaves);
        if (++top == TopBinCount)
            return -1;
        ulong tops = Tops & (ulong.MaxValue << top);
        if (tops == 0)
            return -1;
        top = BitOperations.TrailingZeroCount(tops);
        return top * BinsPerLeaf + BitOperations.TrailingZeroCount(Leaves[top]);
    }

    private static int SizeToBin(ulong size)
    {
        if (size < BinsPerLeaf)
            return (int)size;
        int shift = BitOperations.Log2(size) - 5;
        return (shift + 1) * BinsPerLeaf + (int)((size >> shift) & (BinsPerLeaf - 1));
    }

    private static ulong Padding(ulong offset, ulong alignment) => unchecked(0ul - offset) & (alignment - 1);

    private void InsertFree(uint token)
    {
        ref Node node = ref Nodes[token];
        int bin = SizeToBin(node.Size);
        node.Used = false;
        node.BinPrevious = Invalid;
        node.BinNext = Bins[bin];
        if (node.BinNext != Invalid)
            Nodes[node.BinNext].BinPrevious = token;
        Bins[bin] = token;
        Leaves[bin / BinsPerLeaf] |= 1u << (bin % BinsPerLeaf);
        Tops |= 1ul << (bin / BinsPerLeaf);
    }

    private void RemoveFree(uint token)
    {
        Node node = Nodes[token];
        Debug.Assert(!node.Used);
        if (node.BinPrevious != Invalid)
            Nodes[node.BinPrevious].BinNext = node.BinNext;
        else
        {
            int bin = SizeToBin(node.Size);
            Bins[bin] = node.BinNext;
            if (node.BinNext == Invalid)
            {
                int top = bin / BinsPerLeaf;
                Leaves[top] &= ~(1u << (bin % BinsPerLeaf));
                if (Leaves[top] == 0)
                    Tops &= ~(1ul << top);
            }
        }
        if (node.BinNext != Invalid)
            Nodes[node.BinNext].BinPrevious = node.BinPrevious;
    }

    private uint AcquireNode()
    {
        Debug.Assert(FreeHead != Invalid);
        uint token = FreeHead;
        FreeHead = Nodes[token].BinNext;
        --FreeCount;
        return token;
    }

    private void ReleaseNode(uint token)
    {
        Nodes[token] = new Node { BinNext = FreeHead };
        FreeHead = token;
        ++FreeCount;
    }

    private void EnsureFreeNodes(uint count)
    {
        if (FreeCount >= count)
            return;
        int previousLength = Nodes.Length;
        Array.Resize(ref Nodes, checked(previousLength * 2));
        AddFreeNodes(previousLength);
    }

    private void AddFreeNodes(int first)
    {
        for (int index = Nodes.Length - 1; index >= first; --index)
            ReleaseNode((uint)index);
    }
}
