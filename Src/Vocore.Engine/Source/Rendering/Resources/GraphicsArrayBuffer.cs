using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public class GraphicsArrayBuffer<T> : ShaderResource where T : unmanaged
{
    private readonly GPUDevice _device;
    private readonly GPUBuffer _buffer;
    private readonly GPUResourceGroup _resources;
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
            return _resources;
        }
    }

    internal GraphicsArrayBuffer(GPUDevice device, GPUBuffer buffer, int length)
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

        _data = new NativeBuffer<T>(length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void UpdateBuffer()
    {
        if (_isDirty)
        {
            _device.WriteBuffer(_buffer, (byte*)_data.VoidPtr, (uint)_data.Length * (uint)sizeof(T));
            _isDirty = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        _buffer.Dispose();
        _resources.Dispose();
        _data.Dispose();
    }
}