using System;
using System.Text;
using System.Text.RegularExpressions;

using Veldrid;
using Veldrid.SPIRV;

namespace Vocore.Engine
{
    internal class EngineShaderContext
    {
        private readonly Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();
        private readonly GraphicsDevice _device;
        public BaseVirtualDirectory SourceLibs { get; private set; }
        public BaseVirtualDirectory SourceGraphics { get; private set; }
        public BaseVirtualDirectory SourceCompute { get; private set; }
        public ShaderComplier Complier { get; private set; }

        public EngineShaderContext(GraphicsDevice device)
        {
            _device = device;

            SourceLibs = new BaseVirtualDirectory();
            SourceGraphics = new BaseVirtualDirectory();
            SourceCompute = new BaseVirtualDirectory();

            Complier = new ShaderComplier(_device, SourceLibs);
        }

        public Shader? Get(string name)
        {
            if (_shaders.TryGetValue(name, out var shader))
            {
                return shader;
            }
            Log.Error($"Shader {name} not found in pool");
            return null;
        }

        public bool TryGet(string name, out Shader? shader)
        {
            if (_shaders.TryGetValue(name, out shader))
            {
                return true;
            }
            return false;
        }

        public void Add(string name, Shader shader)
        {
            if (shader == null)
            {
                throw new ArgumentNullException(nameof(shader));
            }

            if (_shaders.ContainsKey(name))
            {
                Log.Error($"Shader {name} already exists in pool");
                return;
            }
            _shaders.Add(name, shader);
        }
    }
}