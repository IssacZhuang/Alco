using System;
using System.Collections.Generic;

namespace Vocore.Unsafe;

public unsafe class MiniHeap<T> : IDisposable where T : unmanaged
{
    public const int BlockSize = 4096;
    public struct Block
    {
        public fixed byte data[BlockSize];
    }

    private NativeBuffer<nint> _blocks;
    public void Dispose()
    {

    }
}