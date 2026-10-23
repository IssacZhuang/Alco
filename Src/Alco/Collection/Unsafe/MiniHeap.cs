using System;
using System.Collections.Generic;

using static Alco.MemoryUtility;

namespace Alco;

/// <summary>
/// A bump allocator for unmanaged values that hands out stable pointers in fixed-size native blocks;
/// the memory is released on <see cref="Dispose"/>.
/// </summary>
public unsafe struct MiniHeap<T> : IDisposable where T : unmanaged
{
    public const int BlockSize = 1024 * 8;
    /// <summary>
    /// The number of elements of type <typeparamref name="T"/> that fit in one block.
    /// </summary>
    public static readonly int ItemPerBlock = BlockSize / sizeof(T);
    /// <summary>
    /// The size in bytes of one element of type <typeparamref name="T"/>.
    /// </summary>
    public static readonly int Stride = sizeof(T);
    /// <summary>
    /// A fixed-size native block that backs heap allocations.
    /// </summary>
    public struct Block
    {
        public fixed byte data[BlockSize];
    }

    private NativeArrayList<nint> _blocks;
    private int _blockIndex;
    private int _indexInBlock;


    /// <summary>
    /// Copies an element into the heap and returns a stable pointer to it.
    /// </summary>
    /// <param name="element">The element to store.</param>
    /// <returns>A pointer to the stored element, valid until the heap is disposed.</returns>
    public T* Alloc(T element)
    {
        EnsureSize();
        Block* block = (Block*)_blocks.UnsafePointer[_blockIndex];
        T* ptr = ((T*)block) + _indexInBlock;
        *ptr = element;
        if (++_indexInBlock >= ItemPerBlock)
        {
            _blockIndex++;
            _indexInBlock = 0;
        }

        return ptr;
    }

    /// <summary>
    /// Reset the index to 0 but not free the memory
    /// </summary>
    public void Reset()
    {
        _indexInBlock = 0;
        _blockIndex = 0;
    }

    private void EnsureSize()
    {
        if (_blockIndex >= _blocks.Length)
        {
            Block* block = Alloc<Block>(1);
            _blocks.Add((nint)block);
        }
    }

    /// <summary>
    /// Releases all native blocks owned by the heap.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < _blocks.Length; i++)
        {
            Free(_blocks[i]);
        }

        _blocks.Dispose();
    }
}