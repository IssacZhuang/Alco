using System;
using Veldrid;

namespace Vocore.Engine
{
    public struct VertexElement
    {
        public string Name;
        public VertexSematic Semantic;
        public VertexFormat Format;
        public uint Offset;
    }
}