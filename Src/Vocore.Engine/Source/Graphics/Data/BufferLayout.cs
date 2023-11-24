using System;
using Veldrid;
using System.Collections.Generic;

namespace Vocore.Engine
{
    public static partial class BufferLayout
    {
        public static readonly ResourceLayoutDescription Camera = new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ViewProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex|ShaderStages.Fragment)
                );

        public static readonly ResourceLayoutDescription Matrix4x4 = new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ModelBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)
                );

                
    }
}

