using System;
using System.Runtime.CompilerServices;

using Vocore.Unsafe;

using Veldrid;

namespace Vocore.Engine
{
    public class GpuBuffer<T> : IGpuBuffer, IDisposable where T : unmanaged
    {
        private T _value;
        private bool _isDisposed;
        private readonly DeviceBuffer _buffer;
        private readonly GraphicsDevice _device;

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
        public GpuBuffer(GraphicsDevice device, BufferUsage usage = BufferUsage.UniformBuffer)
        {
            _device = device;
            _buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(DeviceBufferHelper.GetUniformBufferSize<T>(), usage));
        }

        ~GpuBuffer()
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
        public virtual void UpdateToGPU(CommandList commandList)
        {
            commandList.UpdateBuffer(_buffer, 0, ref _value);
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