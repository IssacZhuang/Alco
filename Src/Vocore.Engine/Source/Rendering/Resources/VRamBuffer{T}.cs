using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine
{
    public class VRamBuffer<T> : ShaderResource where T : unmanaged
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
        public GPUResourceGroup Resources
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                UpdateBuffer();
                return _resources;
            }
        }

        internal VRamBuffer(GPUDevice device, GPUBuffer buffer, T value)
        {
            _device = device;
            _buffer = buffer;
            Name = buffer.Name;

            _value = value;

            _resources = device.CreateResourceGroup(new ResourceGroupDescriptor
            {
                Layout = device.BindGroupBuffer,
                Resources = new ResourceBindingEntry[]
                {
                    new ResourceBindingEntry(0, buffer),
                },
                Name = buffer.Name
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