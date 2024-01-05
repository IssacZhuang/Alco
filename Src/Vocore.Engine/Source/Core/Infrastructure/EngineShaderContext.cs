using System;
using System.Text;
using System.Text.RegularExpressions;

using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    public class EngineShaderContext
    {
        private readonly Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();
        private readonly GraphicsDevice _device;
        private readonly VirtualDirectory _sourceLibs;
        private readonly VirtualDirectory _sourceGraphics;
        private readonly VirtualDirectory _sourceCompute;
        public ShaderCompiler Complier { get; private set; }

        internal EngineShaderContext(GraphicsDevice device)
        {
            _device = device;

            _sourceLibs = new VirtualDirectory();
            _sourceGraphics = new VirtualDirectory();
            _sourceCompute = new VirtualDirectory();

            Complier = new ShaderCompiler(_device, _sourceLibs);
        }


        /// <summary>
        /// Get shader from the ppol
        /// </summary>
        public Shader? Get(string name)
        {
            if (_shaders.TryGetValue(name, out var shader))
            {
                return shader;
            }
            Log.Error($"Shader {name} not found in pool");
            return null;
        }

        /// <summary>
        /// Try get shader from the pool
        /// </summary>
        public bool TryGetShader(string name, out Shader? shader)
        {
            return _shaders.TryGetValue(name, out shader);
        }

        /// <summary>
        /// Add include file to the pool, which can be used by #include "filename" in shader
        /// </summary>
        public bool TryAddShaderInclude(string filename, byte[] shaderContent)
        {
            return _sourceLibs.TryAddData(filename, shaderContent);
        }

        /// <summary>
        /// Add include file to the pool, which can be used by #include "filename" in shader
        /// </summary>
        public bool TryAddShaderInclude(string filename, string shaderText)
        {
            return TryAddShaderInclude(filename, Encoding.UTF8.GetBytes(shaderText));
        }

        /// <summary>
        /// Complie and add shader to the pool
        /// </summary>
        public Shader? ComplieAndAdd(ShaderCompileDescription input)
        {
            if (_shaders.ContainsKey(input.Filename))
            {
                Log.Error($"Shader {input.Filename} already exist in pool");
                return null;
            }
            try
            {
                Shader shader = Complier.Complie(input);
                _shaders.Add(input.Filename, shader);
                return shader;
            }
            catch (Exception e)
            {
                Log.Error($"Complie Shader {input.Filename} failed: \n{e}");
                return null;
            }
        }
    }
}