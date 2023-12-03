using System;
using Veldrid;

namespace Vocore.Engine
{
    public class VertexBuffer<T> : GpuArrayBuffer<T> where T : unmanaged
    {
        public VertexBuffer(GraphicsDevice device, int capacity) : base(device, capacity, BufferUsage.VertexBuffer)
        {
        }
    }
}