using System;
using System.Collections.Generic;

namespace Vocore.Engine
{
    public interface IMesh
    {
        public IntPtr VertexPtr { get; }
        public IntPtr IndexPtr { get; }
        public uint VertexBufferSize { get; }
        public uint IndexBufferSize { get; }
        public uint IndexCount { get; }
    }
}

