using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public class GraphicsBuffer : ShaderResource
{
    private readonly GPUDevice _device;
    private readonly GPUBuffer _buffer;
    private readonly GPUResourceGroup _resources;

    public override string Name { get; }
    public GPUResourceGroup EntryReadonly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _resources;
    }

    internal GraphicsBuffer(string name = "unnamed_graphics_buffer")
    {
        _device = GetDevice();
        
        _buffer = _device.CreateBuffer(new BufferDescriptor
        {
            Usage = BufferUsage.Uniform | BufferUsage.Storage,
            Size = 0,
            Name = name
        });


        Name = name;

        _resources = _device.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = _device.BindGroupBuffer,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, _buffer),
            }
        });
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

    protected override void Dispose(bool disposing)
    {
        _buffer.Dispose();
        _resources.Dispose();
    }
}