using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The encapsulation of a GPU buffer object and its binding resource group.
/// <br/> The size of buffer is equal to the size of the value type.
/// </summary>
/// <typeparam name="T">The type of the value to store in the buffer.</typeparam>
public class GraphicsValueBuffer<T> : GraphicsBuffer where T : unmanaged
{
    //status
    private T _value;

    public ref T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _value;
    }


    internal unsafe GraphicsValueBuffer(RenderingSystem renderingSystem, string name = "unnamed_graphics_buffer") : this(renderingSystem, default, name)
    {
    }

    internal unsafe GraphicsValueBuffer(RenderingSystem renderingSystem, T value = default, string name = "unnamed_graphics_buffer") : base(renderingSystem, (uint)sizeof(T), name)
    {
        _value = value;
        UpdateBuffer();
    }

    /// <summary>
    /// Update the value from CPU to GPU.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateBuffer()
    {
        _device.WriteBuffer(_buffer, _value);
    }

    /// <summary>
    /// Update the value in CPU and then update the value to GPU.
    /// </summary>
    /// <param name="value">The value to update.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateBuffer(T value)
    {
        _value = value;
        _device.WriteBuffer(_buffer, _value);
    }

}
