using System;
using System.Runtime.InteropServices;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class CrossComplieResult
    {
        public ShaderDescription vertex;
        public ShaderDescription fragment;
        public SpirvReflection reflection;

        public CrossComplieResult(byte[] vs, string vsEntry, byte[] fs, string fsEntry, SpirvReflection reflection)
        {
            this.vertex = new ShaderDescription(ShaderStages.Vertex, vs, vsEntry);
            this.fragment = new ShaderDescription(ShaderStages.Fragment, fs, fsEntry);
            this.reflection = reflection;
        }

        public CrossComplieResult(ShaderDescription vertex, ShaderDescription fragment, SpirvReflection reflection)
        {
            this.vertex = vertex;
            this.fragment = fragment;
            this.reflection = reflection;
        }
    }

    public enum ShaderFormat
    {
        Spirv,
        Dxil,
        Hlsl,
        /// <summary>
        /// Glsl vulkan
        /// </summary>
        Glsl_450,
        /// <summary>
        /// Glsl opengl
        /// </summary>
        Glsl_330,
        /// <summary>
        /// Glsl opengles
        /// </summary>
        Glsl_300,
    }
}