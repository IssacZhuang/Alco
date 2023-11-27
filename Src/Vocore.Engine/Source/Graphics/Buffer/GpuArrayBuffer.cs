using System;
using System.Runtime.CompilerServices;

using Vocore;
using Vocore.Unsafe;

using Veldrid;

namespace Vocore.Engine
{
    public class GpuArrayBuffer<T> : IGpuBuffer, IDisposable where T : unmanaged
    {
        private NativeBuffer<T> _content;
        private bool _isDisposed;
        private DeviceBuffer _buffer;
        private GraphicsDevice _device;
        private uint _sizeInBytes;
        private uint _stride;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref _content.GetRef(index);
            }
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _content.Length;
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

        public GpuArrayBuffer(GraphicsDevice device, int capacity, BufferUsage usage = BufferUsage.UniformBuffer)
        {
            int stride = UtilsMemory.SizeOf<T>();
            _stride = (uint)stride;
            _sizeInBytes = DeviceBufferHelper.GetUniformBufferSize(stride * capacity);
            _device = device;
            _buffer = device.ResourceFactory.CreateBuffer(new BufferDescription(_sizeInBytes, usage));
            _content = new NativeBuffer<T>(capacity);
        }

        ~GpuArrayBuffer()
        {
            Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int index, T value)
        {
            _content[index] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(int index)
        {
            return _content[index];
        }

        /// <summary>
        /// Update the value to the GPU memory.\n
        /// This operation is executed by GraphicsDevice, the buffer will be updated immediately.
        /// </summary>
        public void UpdateToGPUImmediately()
        {
            _device.UpdateBuffer(_buffer, 0, _content.IntPtr, (uint)_content.Length * _stride);
        }

        /// <summary>
        /// Update the value to the GPU memory.\n
        /// This operation is executed by command list, the buffer will be updated when the command list is executed. !!Recommended!!
        /// </summary>
        public void UpdateToGPU(CommandList commandList)
        {
            commandList.UpdateBuffer(_buffer, 0, _content.IntPtr, (uint)_content.Length * _stride);
        }

        public ResourceSet CreateResourceSet(ResourceLayoutDescription layoutDesc)
        {
            ResourceLayout layout = _device.ResourceFactory.CreateResourceLayout(layoutDesc);
            return _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout, _buffer));
        }

        public unsafe T* GetUnsafePtr()
        {
            return _content.DataPtr;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _buffer.Dispose();
            _content.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}