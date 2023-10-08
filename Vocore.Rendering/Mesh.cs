using System;
using Veldrid;
using System.Runtime.CompilerServices;

namespace Vocore.Rendering
{
    public class Mesh : IDisposable
    {

        private NativeBuffer<Vertex> _vertices;
        private NativeBuffer<ushort> _indices;
        private uint _vertexBufferSize;
        private uint _indexBufferSize;
        private uint _indexCount;
        private bool _disposed;

        public uint VertexBufferSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _vertexBufferSize;
        }
        public uint IndexBufferSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _indexBufferSize;
        }
        public uint IndexCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _indexCount;
        }

        public unsafe Mesh(Vertex[] vertices, ushort[] indices)
        {
            UpdateVertices(vertices);
            UpdateIndices(indices);
        }

        ~Mesh()
        {
            if (!_disposed)
            {
                InternalDispose();
            }
        }

        public unsafe void UpdateVertices(Vertex[] vertices)
        {
            _vertices.FastEnsureSize(vertices.Length);
            _vertices = new NativeBuffer<Vertex>(vertices.Length);
            _vertexBufferSize = (uint)vertices.Length * Vertex.SizeInBytes;
            fixed (Vertex* ptr = vertices)
            {
                Vertex* ptrBuffer = _vertices.Ptr;
                for (int i = 0; i < vertices.Length; i++)
                {
                    ptrBuffer[i] = ptr[i];
                }
            }
        }

        public unsafe void UpdateIndices(ushort[] indices)
        {
            _indices.FastEnsureSize(indices.Length);
            _indices = new NativeBuffer<ushort>(indices.Length);
            _indexBufferSize = (uint)indices.Length;
            fixed (ushort* ptr = indices)
            {
                ushort* ptrBuffer = _indices.Ptr;
                for (int i = 0; i < indices.Length; i++)
                {
                    ptrBuffer[i] = ptr[i];
                }
            }
            _indexCount = (uint)indices.Length;
        }

        private void InternalDispose()
        {
            if (!_disposed)
            {
                _vertices.Dispose();
                _indices.Dispose();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            InternalDispose();
            GC.SuppressFinalize(this);
        }
    }
}