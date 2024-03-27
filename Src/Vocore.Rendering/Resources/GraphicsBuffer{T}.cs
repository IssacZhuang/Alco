using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class GraphicsBuffer<T> : ShaderResource where T : unmanaged
{
    private readonly GPUDevice _device;
    private readonly GPUBuffer _buffer;
    private readonly GPUResourceGroup _resourcesReadOnly; // for uniform buffer
    private GPUResourceGroup? _resourcesReadWrite; // for storage buffer, optional

    //status
    private T _value;
    private bool _dirty = true;

    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _value;
        set
        {
            _value = value;
            _dirty = true;
        }
    }

    public string Name { get; }
    public GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            UpdateBuffer();
            return _resourcesReadOnly;
        }
    }

    public GPUResourceGroup EntryReadWrite
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            UpdateBuffer();
            if (_resourcesReadWrite == null)
            {
                _resourcesReadWrite = CreateResourceReadWrite();
            }
            return _resourcesReadWrite;
        }
    }

    public GPUBuffer Buffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _buffer;
    }

    public unsafe GraphicsBuffer(string name = "unnamed_graphics_buffer") : this(default, name)
    {
    }

    public unsafe GraphicsBuffer(T value = default, string name = "unnamed_graphics_buffer")
    {
        _device = GetDevice();
        _buffer = _device.CreateBuffer(
            new BufferDescriptor
            {
                Name = name,
                Size = (ulong)sizeof(T),
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.Storage | BufferUsage.Indirect,
            }
        );

        Name = name;

        _value = value;

        _resourcesReadOnly = CreateResourceReadonly();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateBuffer()
    {
        if (_dirty)
        {
            _device.WriteBuffer(_buffer, _value);
            _dirty = false;
        }
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
            Layout = _device.BindGroupUniformBuffer,
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
