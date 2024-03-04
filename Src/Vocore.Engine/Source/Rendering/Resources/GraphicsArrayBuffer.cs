using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public class GraphicsArrayBuffer<T> : ShaderResource where T : unmanaged
{
    public const uint MaxInstanceCount = 500;
    private readonly GPUDevice _device;
    private readonly GPUBuffer _buffer;
    private readonly GPUResourceGroup _resourcesReadOnly;
    private GPUResourceGroup? _resourcesReadWrite;
    private bool _isDirty;
    private NativeBuffer<T> _data;
    public override string Name { get; }

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
            _isDirty = true;
        }
    }

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

    public unsafe GraphicsArrayBuffer(int length, string name = "unnamed_graphics_array_buffer")
    {
        _device = GetDevice();
        _buffer = _device.CreateBuffer(
            new BufferDescriptor
            {
                Name = name,
                Size = (ulong)(sizeof(T) * length),
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.Storage,
            },
            length
        );
        Name = name;

        _resourcesReadOnly = CreateResourceReadonly();

        _data = new NativeBuffer<T>(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void UpdateBuffer()
    {
        if (_isDirty)
        {
            _device.WriteBuffer(_buffer, (byte*)_data.UnsafePointer, (uint)_data.Length * (uint)sizeof(T));
            _isDirty = false;
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
        _data.Dispose();
    }
}