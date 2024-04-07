namespace Vocore.Rendering;

public partial class RenderingSystem
{
    /// <summary>
    /// Create a graphics buffer.
    /// </summary>
    /// <param name="size">The size of the buffer.</param>
    /// <param name="name">The name of the buffer.</param>
    /// <returns>The created graphics buffer.</returns>
    public GraphicsBuffer CreateGraphicsBuffer(uint size,string name = "unnamed_graphics_buffer")
    {
        return new GraphicsBuffer(_device, size, name);
    }

    /// <summary>
    /// Create a graphics value buffer.
    /// </summary>
    /// <param name="name"> The name of the buffer. </param>
    /// <typeparam name="T"> The type of the value to store in the buffer. </typeparam>
    /// <returns> The created graphics value buffer. </returns>
    public unsafe GraphicsValueBuffer<T> CreateGraphicsValueBuffer<T>(string name = "unnamed_graphics_value_buffer") where T : unmanaged
    {
        return new GraphicsValueBuffer<T>(_device, name);
    }

    /// <summary>
    /// Create a graphics value buffer with a specified value.
    /// </summary>
    /// <param name="value">The initial value of the buffer. </param>
    /// <param name="name">The name of the buffer. </param>
    /// <typeparam name="T">The type of the value to store in the buffer. </typeparam>
    /// <returns> The created graphics value buffer. </returns>
    public unsafe GraphicsValueBuffer<T> CreateGraphicsValueBuffer<T>(T value, string name = "unnamed_graphics_value_buffer") where T : unmanaged
    {
        return new GraphicsValueBuffer<T>(_device, value, name);
    }

    /// <summary>
    /// Create a graphics array buffer with a specified length.
    /// </summary>
    /// <param name="length">The length of the array. </param>
    /// <param name="name">The name of the buffer. </param>
    /// <typeparam name="T">The type of the value to store in the buffer. </typeparam>
    /// <returns> The created graphics array buffer. </returns>
    public unsafe GraphicsArrayBuffer<T> CreateGraphicsArrayBuffer<T>(int length, string name = "unnamed_graphics_array_buffer") where T : unmanaged
    {
        return new GraphicsArrayBuffer<T>(_device, length, name);
    }

    /// <summary>
    /// Create a graphics array buffer with a initial data.
    /// </summary>
    /// <param name="initialData">The initial data of the array. </param>
    /// <param name="name">The name of the buffer. </param>
    /// <typeparam name="T">The type of the value to store in the buffer. </typeparam>
    /// <returns> The created graphics array buffer. </returns>
    public unsafe GraphicsArrayBuffer<T> CreateGraphicsArrayBuffer<T>(IReadOnlyList<T> initialData, string name = "unnamed_graphics_array_buffer") where T : unmanaged
    {
        return new GraphicsArrayBuffer<T>(_device, initialData, name);
    }
}