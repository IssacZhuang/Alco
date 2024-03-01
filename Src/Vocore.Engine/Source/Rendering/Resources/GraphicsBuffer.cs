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

    internal GraphicsBuffer(GPUDevice device, GPUBuffer buffer)
    {
        _device = device;
        _buffer = buffer;
        Name = buffer.Name;

        _resources = device.CreateResourceGroup(new ResourceGroupDescriptor
        {
            Layout = device.BindGroupBuffer,
            Resources = new ResourceBindingEntry[]
            {
                new ResourceBindingEntry(0, buffer),
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