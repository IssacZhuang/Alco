using System;
using System.Collections.Generic;

using static Vocore.Unsafe.UtilsMemory;

namespace Vocore.Unsafe;

public unsafe struct NativeChunkList<T> : IDisposable where T : unmanaged
{
    public const int BlockSize = 4096;
    public static readonly int ItemPerBlock = BlockSize / sizeof(T);
    public static readonly int Stride = sizeof(T);
    public struct Block
    {
        public fixed byte data[BlockSize];
        public T* self;
        public int length;
        
        public T this[int index]
        {
            get
            {
                if (index >= length)
                {
                    throw new InvalidOperationException("Index out of range");
                }

                return self[index];
            }
            set
            {
                if (index >= length)
                {
                    throw new InvalidOperationException("Index out of range");
                }

                self[index] = value;
            }
        }

        public void Add(T element)
        {
            self[length] = element;
            length++;
        }
    }



    private NativeArrayList<nint> _blocks;
    private int _count;
    private int _currentBlockIndex;

    public T this[int index]
    {
        get
        {
            if (index >= _count)
            {
                throw new InvalidOperationException("Index out of range");
            }

            return *Access(index);
        }
        set
        {
            if (index >= _count)
            {
                throw new InvalidOperationException("Index out of range");
            }

            *Access(index) = value;
        }
    }

    public void Add(T element)
    {
        EnsureSize();
        Block* block = (Block*)_blocks.UnsafePointer[_currentBlockIndex];
        block->Add(element);
        if (block->length >= ItemPerBlock)
        {
            _currentBlockIndex++;
        }
        _count++;
    }

    public void Remove(T element)
    {
        for (int i = 0; i < _count; i++)
        {
            T value = *Access(i);
            if (value.Equals(element))
            {
                RemoveByIndex(i);
                return;
            }
        }
    }

    public void RemoveByIndex(int index)
    {
        if (index >= _count)
        {
            throw new InvalidOperationException("Index out of range");
        }

        //move last element to index
        _count--;
        if (index != _count)
        {
            *Access(index) = *Access(_count);
        }
    }

    public void Replace(int index, T element)
    {
        if (index >= _count)
        {
            throw new InvalidOperationException("Index out of range");
        }

        *Access(index) = element;
    }

    public void Clear()
    {
        _count = 0;
    }

    private T* Access(int index)
    {
        int blockIndex = index / ItemPerBlock;
        int itemIndex = index % ItemPerBlock;

        T* ptr = (T*)_blocks[blockIndex];
        return &ptr[itemIndex];
    }

    private void EnsureSize()
    {
        if (_currentBlockIndex >= _blocks.Length)
        {
            AddBlock();
        }
    }

    private void AddBlock()
    {
        Block* block = Alloc<Block>(1);
        block->self = (T*)block;
        block->length = 0;
        _blocks.Add((nint)block);
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