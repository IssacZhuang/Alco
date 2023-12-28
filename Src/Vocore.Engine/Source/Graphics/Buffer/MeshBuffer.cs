using System;
using Veldrid;
using Vocore.Engine;

namespace Vocore
{
    public class MeshBuffer : IMeshResource
    {
        private DeviceBuffer _vertexBuffer;
        private DeviceBuffer _indexBuffer;
        private IndexFormat _indexFormat;
        private uint _indexCount;

        public DeviceBuffer VertexBuffer
        {
            get => _vertexBuffer;
        }

        public DeviceBuffer IndexBuffer
        {
            get => _indexBuffer;
        }

        public IndexFormat IndexFormat
        {
            get => _indexFormat;
        }

        public uint IndexCount
        {
            get => _indexCount;
        }

        public MeshBuffer(GraphicsDevice device, IntPtr vertexPtr, uint vertexBufferSize, IntPtr indexPtr, uint indexBufferSize, uint indexCount, IndexFormat indexFormat)
        {
            _vertexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(vertexBufferSize, BufferUsage.VertexBuffer));
            _indexBuffer = device.ResourceFactory.CreateBuffer(new BufferDescription(indexBufferSize, BufferUsage.IndexBuffer));
            device.UpdateBuffer(_vertexBuffer, 0, vertexPtr, vertexBufferSize);
            device.UpdateBuffer(_indexBuffer, 0, indexPtr, indexBufferSize);
            _indexFormat = indexFormat;
            _indexCount = indexCount;
        }

        public MeshBuffer(GraphicsDevice device, IMeshData data): this(device, data.VertexPtr, data.VertexBufferSize, data.IndexPtr, data.IndexBufferSize, data.IndexCount, data.IndexFormat)
        {
        }

        public void Dispose()
        {
            _vertexBuffer.Dispose();
            _indexBuffer.Dispose();
        }
    }
}