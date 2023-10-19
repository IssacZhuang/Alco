using System;
using System.Collections.Generic;

#pragma warning disable CS8601

namespace Vocore.Engine
{
    public static class ShaderPool
    {
        public readonly static BaseVirtualDirectory SourceLibs = new BaseVirtualDirectory();
        public readonly static BaseVirtualDirectory SourceGraphics = new BaseVirtualDirectory();
        public readonly static BaseVirtualDirectory SourceCompute = new BaseVirtualDirectory();
        private readonly static Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();

        public static Shader? Get(string name)
        {
            if (_shaders.TryGetValue(name, out var shader))
            {
                return shader;
            }
            Log.Error($"Shader {name} not found in pool");
            return null;
        }
    
        public static bool TryGet(string name, out Shader shader)
        {
            if (_shaders.TryGetValue(name, out shader))
            {
                return true;
            }
            return false;
        }

        public static void Add(string name, Shader shader)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (_shaders.ContainsKey(name))
            {
                Log.Error($"Shader {name} already exists in pool");
                return;
            }
            _shaders.Add(name, shader);
        }
    }
}

