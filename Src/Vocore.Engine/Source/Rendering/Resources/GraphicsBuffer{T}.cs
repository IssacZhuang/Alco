using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine
{
    public class GraphicsBuffer<T> : ShaderResource where T : unmanaged
    {
        private readonly GPUDevice _device;
        private readonly GPUBuffer _buffer;
        private readonly GPUResourceGroup _resources;

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

        public override string Name { get; }
        public GPUResourceGroup EntryReadonly
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                UpdateBuffer();
                return _resources;
            }
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
                    Usage = BufferUsage.Uniform | BufferUsage.CopyDst
                }
            );

            Name = name;

            _value = value;

            _resources = _device.CreateResourceGroup(new ResourceGroupDescriptor
            {
                Layout = _device.BindGroupBuffer,
                Resources = new ResourceBindingEntry[]
                {
                    new ResourceBindingEntry(0, _buffer),
                },
                Name = _buffer.Name
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateBuffer()
        {
            if (_dirty)
            {
                _device.WriteBuffer(_buffer, _value);
                _dirty = false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            _buffer.Dispose();
            _resources.Dispose();
        }
    }
}