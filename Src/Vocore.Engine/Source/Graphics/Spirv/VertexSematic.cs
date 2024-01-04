using System;

namespace Vocore.Engine
{
    public enum VertexSematic
    {
        Unknown = 0,
        /// <summary>
        /// 2D texture coordinates also known as UVs.
        /// </summary>
        Texcoord,
        /// <summary>
        /// Color of the vertex.
        /// </summary>
        Color,
        /// <summary>
        /// Normal of the vertex.
        /// </summary>
        Normal,
        /// <summary>
        /// World position of the vertex.
        /// </summary>
        Position,
    }
}