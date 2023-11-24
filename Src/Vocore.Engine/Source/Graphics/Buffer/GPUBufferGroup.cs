using System;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class GPUBufferGroup
    {
        private readonly SpirvReflection _reflection;
        public GPUBufferGroup(Shader shader) : this(shader.Reflection)
        {
        }
        public GPUBufferGroup(SpirvReflection reflection)
        {
            _reflection = reflection;
        }

        
    }
}