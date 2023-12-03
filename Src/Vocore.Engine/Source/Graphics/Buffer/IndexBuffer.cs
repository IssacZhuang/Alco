using System;
using Veldrid;

namespace Vocore.Engine
{
    public class IndexBuffer<T> : GpuArrayBuffer<T> where T : unmanaged
    {
        public IndexBuffer(GraphicsDevice device, int capacity) : base(device, capacity, BufferUsage.IndexBuffer)
        {
        }
    }
}