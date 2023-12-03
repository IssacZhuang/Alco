using System;
using System.Runtime.CompilerServices;
using Veldrid;

namespace Vocore.Engine
{
    public class ComputeArrayBuffer<T> : GpuArrayBuffer<T>, IGpuResource where T : unmanaged
    {
        private readonly ResourceSet _resourceSet;
        public ComputeArrayBuffer(GraphicsDevice device, int capacity, bool isReadonly) :
        base(device, capacity, isReadonly ? BufferUsage.StructuredBufferReadOnly : BufferUsage.StructuredBufferReadWrite)
        {
            ResourceLayout layout = device.ResourceFactory.CreateResourceLayout(isReadonly ? BufferLayout.StructuredReadonly : BufferLayout.StructuredReadWrite);
            _resourceSet = device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(layout, Buffer));
        }

        public ResourceSet ResourceSet
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return _resourceSet;
            }
        }
    }
}