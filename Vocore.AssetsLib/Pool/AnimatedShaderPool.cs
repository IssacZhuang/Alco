using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore.AssetsLib
{
    public class AnimatedShaderPool : Singleton<AnimatedShaderPool>
    {
        private readonly Dictionary<string, Shader> _shaders = new Dictionary<string, Shader>();

        private Shader _animated;
        private Shader _animatedInstanced;

        public void LoadAssetBundle(AssetBundle assets)
        {
            foreach (Shader shader in assets.LoadAllAssets<Shader>())
            {
                _shaders.Add(shader.name, shader);
            }
        }

        public Shader GetShader(string key)
        {
            if(_shaders.TryGetValue(key, out Shader shader))
            {
                return null;
            }
            return shader;
        }

        public Shader Animated
        {
            get
            {
                if (_animated == null) _animated = GetShader("Unlit/Animated");
                if (_animated == null) ExceptionRendering.ShaderNotFound("Vocore/Animated");
                return _animated;
            }
        }

        public Shader AnimatedInstanced
        {
            get
            {
                if (_animatedInstanced == null) _animatedInstanced = GetShader("Unlit/AnimatedInstanced");
                if (_animatedInstanced == null) ExceptionRendering.ShaderNotFound("Vocore/AnimatedInstanced");
                return _animatedInstanced;
            }
        }
    }
}
