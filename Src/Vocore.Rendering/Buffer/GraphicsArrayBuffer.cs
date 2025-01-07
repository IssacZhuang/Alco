using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;
/// <summary>
/// The encapsulation of a GPU buffer object and its binding resource group.
/// <br/>The buffer is operated as an array.
/// </summary>
/// <typeparam name="T">The type of the value to store in the buffer.</typeparam>
public class GraphicsArrayBuffer<T> : GraphicsBuffer where T : unmanaged
{
    public const uint MaxInstanceCount = 500;

    private NativeBuffer<T> _data;

    /// <summary>
    /// The indexer for the array buffer in CPU.
    /// </summary>
    /// <param name="index">The index in the array. </param>
    /// <value>The value of the array at the specified index.</value>
    public unsafe T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return _data[index];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _data[index] = value;
        }
    }

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _data.Length;
    }

    internal unsafe GraphicsArrayBuffer(RenderingSystem renderingSystem, int length, string name = "unnamed_graphics_array_buffer") : base(renderingSystem, (uint)(length * sizeof(T)), name)
    {
        _data = new NativeBuffer<T>(length);
    }

    internal unsafe GraphicsArrayBuffer(RenderingSystem renderingSystem, IReadOnlyList<T> initialData, string name = "unnamed_graphics_array_buffer") : base(renderingSystem, (uint)(initialData.Count * sizeof(T)), name)
    {
        _data = new NativeBuffer<T>(initialData.Count);
        for (int i = 0; i < initialData.Count; i++)
        {
            _data[i] = initialData[i];
        }
        UpdateBuffer();
    }

    /// <summary>
    /// Update the array from CPU to GPU.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void UpdateBuffer()
    {
        _device.WriteBuffer(_buffer, (byte*)_data.UnsafePointer, (uint)_data.Length * (uint)sizeof(T));
    }

    /// <summary>
    /// Update the value from CPU to GPU with a specified range.
    /// </summary>
    /// <param name="start">The start index of the array. </param>
    /// <param name="count">The count of the elements to update. </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void UpdateBufferRanged(uint start, uint count)
    {
        if(count == 0)
        {
            return;
        }
        
        _device.WriteBuffer(_buffer, (byte*)_data.UnsafePointer + start * (uint)sizeof(T), count * (uint)sizeof(T));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        //dispose native resources
        _data.Dispose();
    }
}