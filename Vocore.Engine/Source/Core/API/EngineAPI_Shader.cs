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

        public void AddLibShaderText(string name, byte[] text)
        {
            if (_context.SourceLibs.TryAddData(name, text))
            {
                Log.Info($"Shader lib {name} added");
            }
            else
            {
                Log.Error($"Shader lib {name} already added");
            }
        }

        public void AddGraphicsShaderText(string name, byte[] text)
        {
            if (_context.SourceGraphics.TryAddData(name, text))
            {
                Log.Info($"Shader graphics {name} added");
            }
            else
            {
                Log.Error($"Shader graphics {name} already added");
            }
        }

        public void AddComputeShaderText(string name, byte[] text)
        {
            if (_context.SourceCompute.TryAddData(name, text))
            {
                Log.Info($"Shader compute {name} added");
            }
            else
            {
                Log.Error($"Shader compute {name} already added");
            }
        }



        public string ProcessInclude(string filename, string shaderText)
        {
            return _context.ProcessInclude(filename, shaderText);
        }
    }
}