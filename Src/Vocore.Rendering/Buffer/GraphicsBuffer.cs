using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The encapsulation of a GPU buffer object and its binding resource group.
/// </summary>
public class GraphicsBuffer : ShaderResource
{
    protected readonly GPUDevice _device;
    protected readonly GPUBuffer _buffer;
    private readonly GPUResourceGroup _resourcesReadOnly; // for uniform buffer
    private GPUResourceGroup? _resourcesReadWrite; // for storage buffer, optional

    /// <summary>
    /// The name of the buffer.
    /// </summary>
    /// <value>The name of the buffer.</value>
    public string Name { get; }

    /// <summary>
    /// The entry for binding the buffer as uniform buffer.
    /// </summary>
    /// <value>The GPU resource group to bind.</value>
    public GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resourcesReadOnly;
    }

    /// <summary>
    /// The entry for binding the buffer as storage buffer.
    /// </summary>
    /// <value>The GPU resource group to bind.</value>
    public GPUResourceGroup EntryReadWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_resourcesReadWrite == null)
            {
                _resourcesReadWrite = CreateResourceReadWrite();
            }
            return _resourcesReadWrite;
        }
    }

    /// <summary>
    /// The internal abstracted GPU buffer object.
    /// </summary>
    /// <value></value>
    public GPUBuffer Buffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    internal GraphicsBuffer(RenderingSystem renderingSystem, uint size, string name = "unnamed_graphics_buffer")
    {
        _device = renderingSystem.GraphicsDevice;

        _buffer = _device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.Uniform | BufferUsage.Storage | BufferUsage.CopyDst | BufferUsage.Indirect,
            Size = size,
            Name = name
        });


        Name = name;
        _resourcesReadOnly = CreateResourceReadonly();
    }

    /// <summary>
    /// Update the data to GPU immediately.
    /// </summary>
    /// <param name="data">The data to update. </param>
    /// <param name="offset">The offset in GPU memory. </param>
    /// <typeparam name="T">The type of the data.</typeparam>
    public unsafe void UpdateBuffer<T>(T[] data, uint offset = 0) where T : unmanaged
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

    protected override void Dispose(bool disposing)
    {
        _buffer.Dispose();
        _resourcesReadOnly.Dispose();
        _resourcesReadWrite?.Dispose();
    }
}