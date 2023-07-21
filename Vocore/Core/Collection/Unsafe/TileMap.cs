using System;
using System.Runtime.CompilerServices;

using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using Vocore.Unsafe;

public unsafe struct TileMap<T> : IDisposable where T : unmanaged
{
    private NativeArray<T> _data;
    private int2 _size;
    public readonly T defaultValue;

    public int2 Size => _size;
    public int Width => _size.x;
    public int Height => _size.y;
    public int DataLength => _data.Length;

    public unsafe void* GetUnsafePtr()
    {
        return _data.GetUnsafePtr();
    }

    public TileMap(int width, int height, T defaultValue = default(T))
    {
        _size = new int2(width, height);
        _data = new NativeArray<T>(width * height, Allocator.Persistent);
        this.defaultValue = defaultValue;
    }

    public T this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (InRange(x, y))
            {
                return _data[x + y * Width];
            }
            return defaultValue;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            if (InRange(x, y))
            {
                _data[x + y * Width] = value;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool InRange(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    public void Reset()
    {
        for (int i = 0; i < _data.Length; i++)
        {
            _data[i] = defaultValue;
        }
    }

    public void ResizeNoCopy(int width, int height)
    {
        _data.Dispose();
        _size = new int2(width, height);
        _data = new NativeArray<T>(width * height, Allocator.Persistent);
    }

    public void CopyToComputeBuffer(ComputeBuffer buffer)
    {
        buffer.SetData(_data);
    }


    void IDisposable.Dispose()
    {
        _data.Dispose();
    }
}