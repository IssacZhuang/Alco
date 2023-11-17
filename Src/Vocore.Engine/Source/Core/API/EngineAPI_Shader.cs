using System;
using System.Text;
using Veldrid;

namespace Vocore.Engine
{
    public class EngineAPI_Shader
    {
        private readonly EngineShaderContext _context;
        internal EngineAPI_Shader(EngineShaderContext context)
        {
            _context = context;
        }

        public Shader Complie(string shaderText, string filename = "Unknow", string vertexEntry = "VS", string fragmentEntry = "PS")
        {
            return _context.Complier.Complie(shaderText, filename, vertexEntry, fragmentEntry);
        }

        public bool TryAddShaderInclude(string filename, byte[] shaderContent)
        {
            return _context.SourceLibs.TryAddData(filename, shaderContent);
        }
    }
}