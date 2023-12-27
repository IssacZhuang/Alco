using System;
using Veldrid;

namespace Vocore.Engine
{
    public struct ShaderComplieDescription
    {
        /// <summary>
        /// The shader text in HLSL
        /// </summary>
        public string ShaderText;
        /// <summary>
        /// The filename of the shader
        /// </summary>
        public string Filename;
        /// <summary>
        /// The entry point of the vertex shader
        /// </summary>
        public string VertexEntry;
        /// <summary>
        /// The entry point of the fragment shader
        /// </summary>
        public string FragmentEntry;
        /// <summary>
        /// The macros for the shader
        /// </summary>
        public ShaderMacroDefine[]? Macros;
        /// <summary>
        /// The output description of the shader, it used to output to multiple color attachments <br/>
        /// the output description of the framebuffer will be used if null
        /// </summary>
        public OutputDescription? OutputDescription;

        public ShaderComplieDescription(string shaderText, string filename)
        {
            ShaderText = shaderText;
            Filename = filename;
            VertexEntry = "VS";
            FragmentEntry = "PS";
            Macros = null;
            OutputDescription = null;
        }

        public ShaderComplieDescription(string shaderText, string filename, string vertexEntry, string fragmentEntry, ShaderMacroDefine[]? macros = null, OutputDescription? outputDescription = null)
        {
            ShaderText = shaderText;
            Filename = filename;
            VertexEntry = vertexEntry;
            FragmentEntry = fragmentEntry;
            Macros = macros;
            OutputDescription = outputDescription;
        }
    }
}