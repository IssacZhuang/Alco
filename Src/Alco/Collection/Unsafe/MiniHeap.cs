using System;
using System.Collections.Generic;

using static Alco.UtilsMemory;

namespace Alco;

public unsafe struct MiniHeap<T> : IDisposable where T : unmanaged
{
    public const int BlockSize = 1024 * 8;
    public static readonly int ItemPerBlock = BlockSize / sizeof(T);
    public static readonly int Stride = sizeof(T);
    public struct Block
    {
        public fixed byte data[BlockSize];
    }

    private NativeArrayList<nint> _blocks;
    private int _blockIndex;
    private int _indexInBlock;


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

    public void Dispose()
    {
        for (int i = 0; i < _blocks.Length; i++)
        {
            Free(_blocks[i]);
        }

        _blocks.Dispose();
    }
}