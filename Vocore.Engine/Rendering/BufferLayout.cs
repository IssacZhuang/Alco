using System;
using Veldrid;
using System.Collections.Generic;

namespace Vocore.Engine
{
    public static partial class BufferLayout
    {
        public static readonly ResourceLayoutDescription Camera = new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ViewProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
                );

        public static readonly ResourceLayoutDescription Transform = new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ModelBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
                );
    }
}

