using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The encapsulation of a GPU buffer object and its binding resource group.
/// </summary>
public class GraphicsBuffer : AutoDisposable
{
    protected readonly GPUDevice _device;
    protected readonly GPUBuffer _buffer;

    protected GPUBuffer? _bufferCounter;

    protected GPUResourceGroup? _resourcesReadOnly; // for uniform buffer
    protected GPUResourceGroup? _resourcesReadWrite; // for storage buffer, optional
    protected GPUResourceGroup? _resourcesReadWriteWithCounter; // for storage buffer with counter, optional

    protected GPUBuffer BufferCounter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_bufferCounter == null)
            {
                _bufferCounter = _device.CreateBuffer(new BufferDescriptor
                {
                    Usage = BufferUsage.Storage,
                    Size = sizeof(uint),// todo: impl the real counter struct
                    Name = $"{Name}_counter"
                });
            }

            return _bufferCounter;
        }
    }

    /// <summary>
    /// The name of the buffer.
    /// </summary>
    /// <value>The name of the buffer.</value>
    public string Name { get; }

    /// <summary>
    /// The size of the buffer.
    /// </summary>
    public uint Size
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer.Size;
    }

    /// <summary>
    /// The entry for binding the buffer as uniform buffer.
    /// <br/>[warning] It will throw an exception if the buffer size is larger than the limit(65536). Try use <see cref="EntryReadWrite"/> if you need to bind a large buffer.
    /// </summary>
    /// <value>The GPU resource group to bind.</value>
    /// <exception cref="GraphicsException"></exception>
    public virtual GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            _resourcesReadOnly ??= CreateResourceReadonly();
            return _resourcesReadOnly;
        }
    }

    /// <summary>
    /// The entry for binding the buffer as storage buffer.
    /// </summary>
    /// <value>The GPU resource group to bind.</value>
    public virtual GPUResourceGroup EntryReadWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            _resourcesReadWrite ??= CreateResourceReadWrite();
            return _resourcesReadWrite;
        }
    }

    public virtual GPUResourceGroup EntryReadWriteWithCounter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            _resourcesReadWriteWithCounter ??= CreateResourceReadWriteWithCounter();
            return _resourcesReadWriteWithCounter;
        }
    }

    /// <summary>
    /// The internal abstracted GPU buffer object.
    /// </summary>
    /// <value></value>
    public GPUBuffer NativeBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    internal GraphicsBuffer(RenderingSystem renderingSystem, uint size, string name = "unnamed_graphics_buffer")
    {
        _device = renderingSystem.GraphicsDevice;

        _buffer = _device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.Uniform | BufferUsage.Storage | BufferUsage.CopySrc| BufferUsage.CopyDst | BufferUsage.Indirect,
            Size = size,
            Name = name
        });


        Name = name;

    }

    /// <summary>
    /// Update the data to GPU immediately.
    /// </summary>
    /// <param name="data">The data to update. </param>
    /// <param name="offset">The offset in GPU memory. </param>
    /// <typeparam name="T">The type of the data.</typeparam>
    public unsafe void UpdateBuffer<T>(ReadOnlySpan<T> data, uint offset = 0) where T : unmanaged
    {
        fixed (T* ptr = data)
        {
            _device.WriteBuffer(_buffer, offset, (byte*)ptr, (uint)(data.Length * sizeof(T)));
        }
    }

    /// <summary>
    /// Update the data to GPU immediately.
    /// </summary>
    /// <param name="data">The pointer to the data. </param>
    /// <param name="size">The size of the data. </param>
    /// <param name="offset">The offset in GPU memory. </param>
    public unsafe void UpdateBuffer(byte* data, uint size, uint offset = 0)
    {
        _device.WriteBuffer(_buffer, offset, data, size);
    }

    /// <summary>
    /// Update the data to GPU immediately.
    /// </summary>
    /// <param name="data">The data to update. </param>
    /// <typeparam name="T">The type of the data.</typeparam>
    public unsafe void UpdateBuffer<T>(T data) where T : unmanaged
    {
        _device.WriteBuffer(_buffer, 0, (byte*)&data, (uint)sizeof(T));
    }


    private GPUResourceGroup CreateResourceReadonly()
    {
        return _device.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = _device.BindGroupUniformBuffer,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, _buffer),
            }
        });
    }

    private GPUResourceGroup CreateResourceReadWrite()
    {
        return _device.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = _device.BindGroupStorageBuffer,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, _buffer),
            }
        });
    }

    private GPUResourceGroup CreateResourceReadWriteWithCounter()
    {
        return _device.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = _device.BindGroupStorageBufferWithCounter,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, _buffer),
                new ResourceBindingEntry(1, BufferCounter),
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            //dispose non-private managed resources
            _buffer.Dispose();
            _resourcesReadOnly?.Dispose();
            _resourcesReadWrite?.Dispose();
        }
        
    }
}