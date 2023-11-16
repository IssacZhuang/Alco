using System;
using Veldrid;

namespace Vocore.Engine
{
    public class PluginBuiltInShader : IEnginePlugin
    {
        public int Priority => -1000;

        public void OnInitilize(GameEngine engine, ref GameEngineSetting setting)
        {

            IEnumerable<string> shaderNames = EmbbedResources.GetAllFileNamesWithExtension("hlsl");
            foreach (var shaderName in shaderNames)
            {
                // \ to /
                string parsedShaderName = shaderName.Replace('\\', '/');
                Log.Info($"Loading Shader {parsedShaderName}");
                if (EmbbedResources.IsShaderLib(parsedShaderName, out var filename))
                {
                    //ShaderPool.SourceLibs.TryAddData(filename, EmbbedResources.GetBytes(shaderName));
                    engine.Shader.TryAddShaderInclude(filename, EmbbedResources.GetBytes(shaderName));
                }
            }
        }

        public void OnExit()
        {

        }
    }
}