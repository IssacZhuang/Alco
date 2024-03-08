using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class GraphicsBuffer : ShaderResource
{
    private readonly GPUDevice _device;
    private readonly GPUBuffer _buffer;
    private readonly GPUResourceGroup _resourcesReadOnly; // for uniform buffer
    private GPUResourceGroup? _resourcesReadWrite; // for storage buffer, optional

    public string Name { get; }
    public GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resourcesReadOnly;
    }

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

    internal GraphicsBuffer(string name = "unnamed_graphics_buffer")
    {
        _device = GetDevice();

        _buffer = _device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.Uniform | BufferUsage.Storage | BufferUsage.CopyDst,
            Size = 0,
            Name = name
        });


        Name = name;

        _resourcesReadOnly = CreateResourceReadonly();
    }

    public void Update<T>(T data) where T : unmanaged
    {
        _device.WriteBuffer(_buffer, data);
    }

    public void Update(byte[] data)
    {
        _device.WriteBuffer(_buffer, data);
    }

    public unsafe void Update(byte* data, uint size)
    {
        _device.WriteBuffer(_buffer, data, size);
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