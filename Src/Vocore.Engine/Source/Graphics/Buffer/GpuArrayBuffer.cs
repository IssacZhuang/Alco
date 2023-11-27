using System;
using System.Runtime.CompilerServices;

using Vocore;
using Vocore.Unsafe;

using Veldrid;

namespace Vocore.Engine
{
    public class GpuArrayBuffer<T> : IGpuBuffer, IGpuResource, IDisposable where T : unmanaged
    {
        private NativeBuffer<T> _content;
        private bool _isDisposed;
        private readonly DeviceBuffer _buffer;
        private readonly GraphicsDevice _device;
        private readonly uint _sizeInBytes;
        private readonly uint _stride;
        private readonly ResourceSet _resourceSet;

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

        public ResourceSet ResourceSet
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _resourceSet;
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
            ResourceLayout layout = _device.ResourceFactory.CreateResourceLayout(BufferLayout.Default);
            //_resourceSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout, _buffer));
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