using Vocore.Graphics;

namespace Vocore.Engine;

    public interface IMeshData
    {
        public IntPtr VertexPtr { get; }
        public IntPtr IndexPtr { get; }
        public uint VertexBufferSize { get; }
        public uint IndexBufferSize { get; }
        public uint IndexCount { get; }
        public IndexFormat IndexFormat { get; }
    }

