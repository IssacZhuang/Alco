using System;
using System.Collections.Generic;
using Veldrid;

namespace Vocore.Engine
{
    public interface IMesh
    {
        public IntPtr VertexPtr { get; }
        public IntPtr IndexPtr { get; }
        public uint VertexBufferSize { get; }
        public uint IndexBufferSize { get; }
        public uint IndexCount { get; }
        public IndexFormat IndexFormat { get; }
    }
}

