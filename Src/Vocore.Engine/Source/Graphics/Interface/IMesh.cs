using System;
using System.Collections.Generic;
using Veldrid;

namespace Vocore.Engine
{
    /// <summary>
    /// Mesh data in GPU memory
    /// </summary>
    public interface IMeshResource
    {
        public DeviceBuffer VertexBuffer { get; }
        public DeviceBuffer IndexBuffer { get; }
        public IndexFormat IndexFormat { get; }
        public uint IndexCount { get; }
    }

    /// <summary>
    /// Mesh data only in CPU memory
    /// </summary>
    public interface IMeshData
    {
        public IntPtr VertexPtr { get; }
        public IntPtr IndexPtr { get; }
        public uint VertexBufferSize { get; }
        public uint IndexBufferSize { get; }
        public uint IndexCount { get; }
        public IndexFormat IndexFormat { get; }
    }
}

