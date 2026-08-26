using System.Collections.Concurrent;
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

    // Bind groups that bind this buffer as the only resource of a shader group,
    // keyed by the group layout they were created against. A single-resource
    // group is fully determined by (buffer, layout), so one group per layout
    // is created for the buffer's lifetime and shared across materials and
    // frames instead of being rebuilt on every slot change.
    // Thread safety: reads are lock free; the first creation per key serializes
    // on _createGroupLock, so materials on multiple threads may bind the same
    // buffer concurrently. The lock also guards the lazy counter buffer.
    private readonly ConcurrentDictionary<GPUBindGroup, GPUResourceGroup> _layoutResourceGroups = new();
    private readonly Lock _createGroupLock = new();

    /// <summary>The <paramref name="counterBinding"/> value of
    /// <see cref="GetOrCreateResourceGroup"/> for groups without a counter companion.</summary>
    internal const uint NoCounterBinding = uint.MaxValue;

    protected GPUBuffer BufferCounter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_bufferCounter == null)
            {
                lock (_createGroupLock)
                {
                    _bufferCounter ??= _device.CreateBuffer(new BufferDescriptor
                    {
                        Usage = BufferUsage.Storage,
                        Size = sizeof(uint),// todo: impl the real counter struct
                        Name = $"{Name}_counter"
                    });
                }
            }

            return _bufferCounter;
        }
    }

    /// <summary>
    /// The internal GPU buffer that backs the structured buffer counter, created on demand.
    /// </summary>
    internal GPUBuffer CounterBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BufferCounter;
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
    /// Returns the bind group that binds this buffer as the only resource of a
    /// shader bind group with the given layout, creating it on first use. The
    /// group is cached on the buffer for its lifetime and shared across all
    /// materials and frames, so cycling the buffer through a material (e.g.
    /// per-instance dispatches) does not allocate a new bind group per change.
    /// </summary>
    /// <param name="layout">The bind group layout of the consuming shader's group.</param>
    /// <param name="binding">The binding number of the buffer inside the group.</param>
    /// <param name="counterBinding">The binding number of the structured buffer counter companion, or <see cref="NoCounterBinding"/> when the group has none.</param>
    /// <returns>The cached or newly created resource group.</returns>
    internal GPUResourceGroup GetOrCreateResourceGroup(GPUBindGroup layout, uint binding, uint counterBinding = NoCounterBinding)
    {
        if (_layoutResourceGroups.TryGetValue(layout, out GPUResourceGroup? group))
        {
            return group;
        }

        lock (_createGroupLock)
        {
            if (_layoutResourceGroups.TryGetValue(layout, out group))
            {
                return group;
            }

            ResourceBindingEntry[] entries = counterBinding == NoCounterBinding
                ? new ResourceBindingEntry[] { new(binding, NativeBuffer) }
                : new ResourceBindingEntry[] { new(binding, NativeBuffer), new(counterBinding, CounterBuffer) };
            group = _device.CreateResourceGroup(new ResourceGroupDescriptor(layout, entries, $"{Name}_layout_bind_group"));
            _layoutResourceGroups[layout] = group;
            return group;
        }
    }

    /// <summary>
    /// The internal abstracted GPU buffer object.
    /// <br/>Accessed by the material system when assembling bind groups; subclasses
    /// may override it to flush pending uploads lazily (see <see cref="BaseCameraBuffer{T}"/>).
    /// </summary>
    public virtual GPUBuffer NativeBuffer
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
            _resourcesReadWriteWithCounter?.Dispose();
            foreach (GPUResourceGroup group in _layoutResourceGroups.Values)
            {
                group.Dispose();
            }

            _layoutResourceGroups.Clear();
        }

    }
}