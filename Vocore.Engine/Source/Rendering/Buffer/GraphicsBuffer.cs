using System;
using System.Runtime.CompilerServices;

using Vocore.Unsafe;

using Veldrid;

namespace Vocore.Engine
{
    public class GraphicsBuffer<T> : IGraphicsBuffer, IDisposable where T : unmanaged
    {
        private T _value;
        private bool _isDisposed;
        private DeviceBuffer _buffer;
        private GraphicsDevice _device;

        public T Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _value;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                _value = value;
            }
        }

        public ref T ValueRef
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref _value;
            }
        }

        public DeviceBuffer Buffer
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _buffer;
            }
        }

        /// <summary>
        /// Create a GPU buffer with a value.
        /// </summary>
        public GraphicsBuffer(GraphicsDevice device, BufferUsage usage = BufferUsage.UniformBuffer)
        {
            _device = device;
            _buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(DeviceBufferHelper.GetUniformBufferSize<T>(), usage));
        }

        ~GraphicsBuffer()
        {
            Dispose();
        }

        /// <summary>
        /// Update the value to the GPU memory.\n
        /// This operation is executed by GraphicsDevice, the buffer will be updated immediately.
        /// </summary>
        public void UpdateToGPUImmediately()
        {
            _device.UpdateBuffer(_buffer, 0, ref _value);
        }

        /// <summary>
        /// Update the value to the GPU memory.\n
        /// This operation is executed by command list, the buffer will be updated when the command list is executed. !!Recommended!!
        /// </summary>
        public void UpdateToGPU(CommandList commandList)
        {
            commandList.UpdateBuffer(_buffer, 0, ref _value);
        }

        public ResourceSet CreateResourceSet(ResourceLayoutDescription layoutDesc)
        {
            ResourceLayout layout = _device.ResourceFactory.CreateResourceLayout(layoutDesc);
            return _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout, _buffer));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _buffer.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}