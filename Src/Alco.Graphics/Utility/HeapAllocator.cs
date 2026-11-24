// TLSF-style suballocation of caller-owned storage: 32 top bins x 8 leaf bins, intrusive lists, and neighbor coalescing.

using System.Diagnostics;
using System.Numerics;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

public struct HeapAllocation<T> where T : unmanaged
{
    public GPUAllocation<T> Range; // Size is in bytes.
    public uint Token = 0xffffffffu;

    public HeapAllocation()
    {
    }
}

/// <summary>Suballocates and reuses 16-byte-aligned ranges without owning the backing storage; externally synchronize operations.</summary>
public unsafe class HeapAllocator
{
    public const ulong Alignment = 16;
    public const ulong MaximumSize = 0xffffffffu * Alignment;
    public const uint MaximumAllocationCount = (0xffffffffu - 1) / 2;

    internal const uint UnusedNode = 0xffffffffu;
    private const uint TopBinCount = 32;
    private const uint BinsPerLeaf = 8;
    private const uint LeafBinCount = TopBinCount * BinsPerLeaf;

    // Bin index of a size, rounded down (3-bit mantissa tiers above the small-size cutoff).
    private static uint SizeToBinRoundDown(uint size)
    {
        const uint mantissaBits = 3;
        const uint mantissaValue = 1u << (int)mantissaBits;
        const uint mantissaMask = mantissaValue - 1;

        if (size < mantissaValue)
            return size;

        uint mantissaStartBit = 31u - (uint)BitOperations.LeadingZeroCount(size) - mantissaBits;
        return ((mantissaStartBit + 1) << (int)mantissaBits) | ((size >> (int)mantissaStartBit) & mantissaMask);
    }

    private static uint FindLowestSetBit(uint bits, uint firstBit)
    {
        const uint invalidBin = 0xffffffffu;

        if (firstBit >= 32)
            return invalidBin;

        bits &= uint.MaxValue << (int)firstBit;
        return bits == 0 ? invalidBin : (uint)BitOperations.TrailingZeroCount(bits);
    }

    private static byte* OffsetPointer(byte* pointer, ulong offset)
    {
        if (pointer == null)
            return null;
        return (byte*)((nint)pointer + (nint)offset);
    }

    internal struct Range
    {
        public uint Offset = UnusedNode;
        public uint Token = UnusedNode;

        public Range()
        {
        }
    }

    // Two-level segregated-fit allocator over a fixed range of equally sized elements. Ported member-for-
    // member from HeapAllocator::RangeAllocator; nodes and free lists live in managed arrays.
    internal sealed unsafe class RangeAllocator
    {
        internal struct Node
        {
            public uint Offset = 0;
            public uint Size = 0;
            public uint BinPrevious = UnusedNode;
            public uint BinNext = UnusedNode;
            public uint NeighborPrevious = UnusedNode;
            public uint NeighborNext = UnusedNode;
            public bool Used = false;

            public Node()
            {
            }
        }

        internal ulong ElementSize = 0;
        internal uint Capacity = 0;
        internal uint MaxAllocations = 0;
        internal uint AllocationCount = 0;
        internal uint NodeCapacity = 0;
        internal uint FreeNodeCount = 0;
        internal uint UsedTopBins = 0;
        internal byte[] UsedLeafBins = new byte[TopBinCount];
        internal uint[] BinIndices = new uint[LeafBinCount];
        internal Node[] Nodes = null!;
        internal uint[] FreeNodes = null!;

        internal RangeAllocator(ulong byteSize, uint allocationLimit, ulong allocationElementSize)
        {
            ElementSize = allocationElementSize;
            Debug.Assert(ElementSize != 0 && (ElementSize & (ElementSize - 1)) == 0 && byteSize >= ElementSize);
            Debug.Assert(allocationLimit != 0 && allocationLimit <= MaximumAllocationCount);
            ulong elementCapacity = byteSize / ElementSize;
            Debug.Assert(elementCapacity <= 0xffffffffu);
            Capacity = (uint)elementCapacity;
            MaxAllocations = allocationLimit;
            NodeCapacity = allocationLimit * 2 + 1;
            Nodes = new Node[NodeCapacity];
            FreeNodes = new uint[NodeCapacity];
            Reset();
        }

        internal uint AcquireNode()
        {
            Debug.Assert(FreeNodeCount != 0);
            return FreeNodes[--FreeNodeCount];
        }

        internal void ReleaseNode(uint nodeIndex)
        {
            Debug.Assert(FreeNodeCount < NodeCapacity);
            Nodes[nodeIndex] = new Node();
            FreeNodes[FreeNodeCount++] = nodeIndex;
        }

        internal void InsertFreeNode(uint nodeIndex)
        {
            ref Node node = ref Nodes[nodeIndex];
            uint binIndex = SizeToBinRoundDown(node.Size);
            uint topBinIndex = binIndex / BinsPerLeaf;
            uint leafBinIndex = binIndex % BinsPerLeaf;

            node.Used = false;
            node.BinPrevious = UnusedNode;
            node.BinNext = BinIndices[binIndex];
            if (node.BinNext != UnusedNode)
                Nodes[node.BinNext].BinPrevious = nodeIndex;
            BinIndices[binIndex] = nodeIndex;
            UsedLeafBins[topBinIndex] |= (byte)(1u << (int)leafBinIndex);
            UsedTopBins |= 1u << (int)topBinIndex;
        }

        internal void RemoveFreeNode(uint nodeIndex)
        {
            ref Node node = ref Nodes[nodeIndex];
            Debug.Assert(!node.Used);

            if (node.BinPrevious != UnusedNode)
            {
                Nodes[node.BinPrevious].BinNext = node.BinNext;
                if (node.BinNext != UnusedNode)
                    Nodes[node.BinNext].BinPrevious = node.BinPrevious;
            }
            else
            {
                uint binIndex = SizeToBinRoundDown(node.Size);
                uint topBinIndex = binIndex / BinsPerLeaf;
                uint leafBinIndex = binIndex % BinsPerLeaf;

                Debug.Assert(BinIndices[binIndex] == nodeIndex);
                BinIndices[binIndex] = node.BinNext;
                if (node.BinNext != UnusedNode)
                    Nodes[node.BinNext].BinPrevious = UnusedNode;
                else
                {
                    UsedLeafBins[topBinIndex] &= (byte)~(1u << (int)leafBinIndex);
                    if (UsedLeafBins[topBinIndex] == 0)
                        UsedTopBins &= ~(1u << (int)topBinIndex);
                }
            }

            node.BinPrevious = UnusedNode;
            node.BinNext = UnusedNode;
        }

        internal Range Allocate(ulong byteSize)
        {
            Debug.Assert(byteSize != 0);
            if (Nodes == null || AllocationCount == MaxAllocations)
                return new Range();

            ulong requestedElements = 1 + (byteSize - 1) / ElementSize;
            if (requestedElements > Capacity)
                return new Range();
            uint size = (uint)requestedElements;
            uint approximateBinIndex = SizeToBinRoundDown(size);
            uint nodeIndex = BinIndices[approximateBinIndex];
            while (nodeIndex != UnusedNode && Nodes[nodeIndex].Size < size)
                nodeIndex = Nodes[nodeIndex].BinNext;

            if (nodeIndex == UnusedNode)
            {
                uint minimumBinIndex = approximateBinIndex + 1;
                uint topBinIndex = minimumBinIndex / BinsPerLeaf;
                uint leafBinIndex = 0xffffffffu;
                if (topBinIndex < TopBinCount)
                    leafBinIndex = FindLowestSetBit(UsedLeafBins[topBinIndex], minimumBinIndex % BinsPerLeaf);

                if (leafBinIndex == 0xffffffffu)
                {
                    topBinIndex = FindLowestSetBit(UsedTopBins, topBinIndex + 1);
                    if (topBinIndex == 0xffffffffu)
                        return new Range();
                    leafBinIndex = (uint)BitOperations.TrailingZeroCount(UsedLeafBins[topBinIndex]);
                }
                nodeIndex = BinIndices[topBinIndex * BinsPerLeaf + leafBinIndex];
            }
            ref Node node = ref Nodes[nodeIndex];
            uint originalSize = node.Size;
            uint originalNextNeighbor = node.NeighborNext;
            Debug.Assert(originalSize >= size);
            RemoveFreeNode(nodeIndex);

            node.Size = size;
            node.Used = true;
            ++AllocationCount;

            if (originalSize != size)
            {
                uint remainderIndex = AcquireNode();
                ref Node remainder = ref Nodes[remainderIndex];
                remainder.Offset = node.Offset + size;
                remainder.Size = originalSize - size;
                remainder.NeighborPrevious = nodeIndex;
                remainder.NeighborNext = originalNextNeighbor;
                if (originalNextNeighbor != UnusedNode)
                    Nodes[originalNextNeighbor].NeighborPrevious = remainderIndex;
                node.NeighborNext = remainderIndex;
                InsertFreeNode(remainderIndex);
            }

            return new Range { Offset = node.Offset, Token = nodeIndex };
        }

        internal void Free(uint token)
        {
            Debug.Assert(Nodes != null && token < NodeCapacity);
            ref Node node = ref Nodes[token];
            Debug.Assert(node.Used);

            if (node.NeighborPrevious != UnusedNode && !Nodes[node.NeighborPrevious].Used)
            {
                uint previousIndex = node.NeighborPrevious;
                RemoveFreeNode(previousIndex);
                node.Offset = Nodes[previousIndex].Offset;
                node.Size += Nodes[previousIndex].Size;
                node.NeighborPrevious = Nodes[previousIndex].NeighborPrevious;
                ReleaseNode(previousIndex);
            }

            if (node.NeighborNext != UnusedNode && !Nodes[node.NeighborNext].Used)
            {
                uint nextIndex = node.NeighborNext;
                RemoveFreeNode(nextIndex);
                node.Size += Nodes[nextIndex].Size;
                node.NeighborNext = Nodes[nextIndex].NeighborNext;
                ReleaseNode(nextIndex);
            }

            if (node.NeighborPrevious != UnusedNode)
                Nodes[node.NeighborPrevious].NeighborNext = token;
            if (node.NeighborNext != UnusedNode)
                Nodes[node.NeighborNext].NeighborPrevious = token;

            InsertFreeNode(token);
            --AllocationCount;
        }

        internal void Reset()
        {
            if (Nodes == null)
                return;
            Debug.Assert(FreeNodes != null && Capacity != 0 && MaxAllocations != 0);
            AllocationCount = 0;
            FreeNodeCount = NodeCapacity;
            UsedTopBins = 0;
            Array.Clear(UsedLeafBins);
            for (uint index = 0; index != LeafBinCount; ++index)
                BinIndices[index] = UnusedNode;
            for (uint index = 0; index != NodeCapacity; ++index)
            {
                Nodes[index] = new Node();
                FreeNodes[index] = index;
            }

            uint nodeIndex = AcquireNode();
            Nodes[nodeIndex].Size = Capacity;
            InsertFreeNode(nodeIndex);
        }
    }

    private GPUAllocation<byte> _storage;
    private RangeAllocator _ranges;

    /// <summary>Creates an allocator over CPU-only or buffer-backed storage, leaving any trailing partial alignment block unused.</summary>
    /// <param name="storage">Storage of 16 through MaximumSize bytes with a 16-byte-aligned CPU mapping and backing-buffer offset when present.</param>
    /// <param name="maxAllocations">The allocation limit, from one through <see cref="MaximumAllocationCount"/>.</param>
    public HeapAllocator(GPUAllocation<byte> storage, uint maxAllocations)
    {
        Debug.Assert(storage.Cpu != null || storage.Buffer != null);
        Debug.Assert(storage.SizeBytes <= MaximumSize);
        Debug.Assert((storage.Cpu == null || (nint)storage.Cpu % (nint)Alignment == 0) &&
                     (storage.Buffer == null || storage.Offset % Alignment == 0));
        _storage = storage;
        _ranges = new RangeAllocator(storage.SizeBytes, maxAllocations, Alignment);
    }

    /// <summary>Allocates bytes with a reservation rounded up to 16 bytes.</summary>
    /// <param name="byteSize">The positive requested size in bytes.</param>
    /// <returns>The requested allocation and release token, or a zero-sized allocation when storage is exhausted.</returns>
    public HeapAllocation<byte> Allocate(ulong byteSize)
    {
        Range allocation = _ranges.Allocate(byteSize);
        if (allocation.Offset == UnusedNode)
            return new HeapAllocation<byte>();
        ulong byteOffset = allocation.Offset * _ranges.ElementSize;
        return new HeapAllocation<byte>
        {
            Range = new GPUAllocation<byte>
            {
                Cpu = OffsetPointer(_storage.Cpu, byteOffset),
                Buffer = _storage.Buffer,
                Offset = _storage.Offset + byteOffset,
                SizeBytes = byteSize,
            },
            Token = allocation.Token,
        };
    }

    /// <summary>Allocates contiguous elements with a reservation rounded up to 16 bytes.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="elementCount">The positive number of elements to allocate.</param>
    /// <returns>The requested allocation and release token, or a zero-sized allocation when storage is exhausted.</returns>
    public HeapAllocation<T> Allocate<T>(ulong elementCount) where T : unmanaged
    {
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

    /// <summary>Returns a previously allocated range for reuse.</summary>
    /// <typeparam name="T">The allocation's unmanaged element type.</typeparam>
    /// <param name="allocation">An allocation from this allocator that has not already been freed or invalidated by Reset.</param>
    public void Free<T>(HeapAllocation<T> allocation) where T : unmanaged
    {
        _ranges.Free(allocation.Token);
    }

    /// <summary>Invalidates every previous allocation and makes the entire storage reusable.</summary>
    public void Reset()
    {
        _ranges.Reset();
    }
}
