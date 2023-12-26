using System;
using Veldrid;

namespace Vocore.Engine
{
    public struct ShaderComplieDescription
    {
        public string ShaderText;
        public string Filename;
        public string VertexEntry;
        public string FragmentEntry;
        public ShaderMacroDefine[]? Macros;
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