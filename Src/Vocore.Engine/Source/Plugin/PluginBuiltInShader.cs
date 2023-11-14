using System;
using Veldrid;

namespace Vocore.Engine
{
    public class PluginBuiltInShader : IEnginePlugin
    {
        public int Priority => -1000;

        public void OnInitilize(GameEngine engine, ref GameEngineSetting setting)
        {

            IEnumerable<string> shaderNames = EmbbedResources.GetAllFileNamesWithExtension("glsl");
            foreach (var shaderName in shaderNames)
            {
                // \ to /
                string parsedShaderName = shaderName.Replace('\\', '/');
                Log.Info($"Loading Shader {parsedShaderName}");
                if (EmbbedResources.IsShaderLib(parsedShaderName, out var filename))
                {
                    //ShaderPool.SourceLibs.TryAddData(filename, EmbbedResources.GetBytes(shaderName));
                    engine.Shader.AddLibShaderText(filename, EmbbedResources.GetBytes(shaderName));
                }
                else if (EmbbedResources.IsGraphicsShader(parsedShaderName, out filename))
                {
                    //ShaderPool.SourceGraphics.TryAddData(filename, EmbbedResources.GetBytes(shaderName));
                    engine.Shader.AddGraphicsShaderText(filename, EmbbedResources.GetBytes(shaderName));
                }
                else if (EmbbedResources.IsComputeShader(parsedShaderName, out filename))
                {
                    //ShaderPool.SourceCompute.TryAddData(filename, EmbbedResources.GetBytes(shaderName));
                    engine.Shader.AddComputeShaderText(filename, EmbbedResources.GetBytes(shaderName));
                }
            }
        }

        public void OnExit()
        {

        }
    }
}