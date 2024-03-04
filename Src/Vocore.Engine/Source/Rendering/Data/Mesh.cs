using System;
using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine
{
    public class Mesh : IMeshData, IDisposable
    {

        private NativeBuffer<Vertex> _vertices;
        private NativeBuffer<ushort> _indices;
        private uint _vertexBufferSize;
        private uint _indexBufferSize;
        private uint _indexCount;
        private bool _disposed;
        private IntPtr _vertexPtr;
        private IntPtr _indexPtr;

        public unsafe IntPtr VertexPtr
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _vertexPtr;
        }

        public unsafe IntPtr IndexPtr
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _indexPtr;
        }

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

        public IndexFormat IndexFormat
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => IndexFormat.Uint16;
        }

        public unsafe Mesh(Vertex[] vertices, ushort[] indices)
        {
            UpdateVertices(vertices);
            UpdateIndices(indices);
        }

        ~Mesh()
        {
            InternalDispose();
        }

        public unsafe void UpdateVertices(Vertex[] vertices)
        {
            _vertices.EnsureSizeNoCopy(vertices.Length);
            _vertexBufferSize = (uint)vertices.Length * Vertex.SizeInBytes;
            fixed (Vertex* ptr = vertices)
            {
                Vertex* ptrBuffer = _vertices.UnsafePointer;
                for (int i = 0; i < vertices.Length; i++)
                {
                    ptrBuffer[i] = ptr[i];
                }
            }
            _vertexPtr = (IntPtr)_vertices.UnsafePointer;
        }

        public unsafe void UpdateIndices(ushort[] indices)
        {
            _indices.EnsureSizeNoCopy(indices.Length);
            _indexBufferSize = (uint)indices.Length * sizeof(ushort);
            fixed (ushort* ptr = indices)
            {
                ushort* ptrBuffer = _indices.UnsafePointer;
                for (int i = 0; i < indices.Length; i++)
                {
                    ptrBuffer[i] = ptr[i];
                }
            }
            
            _indexCount = (uint)indices.Length;
            _indexPtr = (IntPtr)_indices.UnsafePointer;
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