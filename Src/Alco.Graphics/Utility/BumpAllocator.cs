// Linear and atomic suballocation of caller-owned CPU mappings and buffer ranges.

using System.Diagnostics;

using Alco.Graphics;

namespace Alco.Graphics.LowLevel;

/// <summary>Suballocates 16-byte-aligned ranges without owning the backing storage.</summary>
public unsafe class BumpAllocator
{
    public const ulong Alignment = 16;

    private GPUAllocation<byte> _storage;
    private ulong _offset; // C++ keeps this 8-aligned so 64-bit atomics stay lock free; ulong fields already are.

    /// <summary>Creates an allocator over nonempty CPU-only or buffer-backed storage.</summary>
    /// <param name="storage">Storage whose CPU mapping and backing-buffer offset, when present, are 16-byte aligned.</param>
    public BumpAllocator(GPUAllocation<byte> storage)
    {
        Debug.Assert(storage.SizeBytes != 0);
        Debug.Assert(storage.Cpu != null || storage.Buffer != null);
        Debug.Assert((storage.Cpu == null || (nint)storage.Cpu % (nint)Alignment == 0) &&
                     (storage.Buffer == null || storage.Offset % Alignment == 0));
        _storage = storage;
    }

    /// <summary>Allocates bytes with a reservation rounded up to 16 bytes.</summary>
    /// <param name="byteSize">The positive requested size in bytes.</param>
    /// <returns>The requested allocation, or a zero-sized allocation when storage is exhausted.</returns>
    public GPUAllocation<byte> Allocate(ulong byteSize)
    {
        Debug.Assert(byteSize != 0);
        if (byteSize > _storage.SizeBytes - _offset)
            return default;

        GPUAllocation<byte> allocation = new()
        {
            Cpu = OffsetPointer(_storage.Cpu, _offset),
            Buffer = _storage.Buffer,
            Offset = _storage.Offset + _offset,
            SizeBytes = byteSize,
        };
        ulong remaining = _storage.SizeBytes - _offset;
        ulong alignedSize = (byteSize + Alignment - 1) & ~(Alignment - 1);
        _offset += alignedSize < remaining ? alignedSize : remaining;
        return allocation;
    }

    // Every other operation, including Allocate(), Reset(), and destruction, requires exclusive access.
    // None may execute concurrently with Allocate() or AllocateAtomic().
    /// <summary>Allocates disjoint ranges concurrently with reservations rounded up to 16 bytes.</summary>
    /// <param name="byteSize">The positive requested size in bytes.</param>
    /// <returns>The requested allocation, or a zero-sized allocation when storage is exhausted.</returns>
    public GPUAllocation<byte> AllocateAtomic(ulong byteSize)
    {
        Debug.Assert(byteSize != 0);
        ulong allocationOffset = Volatile.Read(ref _offset);
        for (;;)
        {
            if (byteSize > _storage.SizeBytes - allocationOffset)
                return default;

            ulong remaining = _storage.SizeBytes - allocationOffset;
            ulong alignedSize = (byteSize + Alignment - 1) & ~(Alignment - 1);
            ulong nextOffset = allocationOffset + (alignedSize < remaining ? alignedSize : remaining);
            ulong previousOffset = Interlocked.CompareExchange(ref _offset, nextOffset, allocationOffset);
            if (previousOffset == allocationOffset)
            {
                return new GPUAllocation<byte>
                {
                    Cpu = OffsetPointer(_storage.Cpu, allocationOffset),
                    Buffer = _storage.Buffer,
                    Offset = _storage.Offset + allocationOffset,
                    SizeBytes = byteSize,
                };
            }
            allocationOffset = previousOffset;
        }
    }

    /// <summary>Allocates contiguous elements with a reservation rounded up to 16 bytes.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="elementCount">The positive number of elements to allocate.</param>
    /// <returns>The requested allocation, or a zero-sized allocation when storage is exhausted.</returns>
    public GPUAllocation<T> Allocate<T>(ulong elementCount = 1) where T : unmanaged
    {
        GPUAllocation<byte> allocation = Allocate(elementCount * (ulong)sizeof(T));
        return new GPUAllocation<T> { Cpu = (T*)allocation.Cpu, Buffer = allocation.Buffer, Offset = allocation.Offset, SizeBytes = allocation.SizeBytes };
    }

    /// <summary>Allocates disjoint element ranges concurrently with reservations rounded up to 16 bytes.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="elementCount">The positive number of elements to allocate.</param>
    /// <returns>The requested allocation, or a zero-sized allocation when storage is exhausted.</returns>
    public GPUAllocation<T> AllocateAtomic<T>(ulong elementCount = 1) where T : unmanaged
    {
        GPUAllocation<byte> allocation = AllocateAtomic(elementCount * (ulong)sizeof(T));
        return new GPUAllocation<T> { Cpu = (T*)allocation.Cpu, Buffer = allocation.Buffer, Offset = allocation.Offset, SizeBytes = allocation.SizeBytes };
    }

    /// <summary>Invalidates every previous allocation and makes the entire storage reusable.</summary>
    public void Reset()
    {
        _offset = 0;
    }

    private static byte* OffsetPointer(byte* pointer, ulong offset)
    {
        if (pointer == null)
            return null;
        return (byte*)((nint)pointer + (nint)offset);
    }
}
