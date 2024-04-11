using System;

namespace Vocore.Engine
{
    public class PluginBuiltInShader : BaseEnginePlugin
    {
        private class ShaderLibSource : IFileSource
        {
            public int Order => 0;
            public IEnumerable<string> AllFileNames => EmbbedResources.AllFileNames;

            public void OnUnload()
            {
                
            }

            public bool TryGetData(string path, out ReadOnlySpan<byte> data)
            {
                data = EmbbedResources.GetBytes(path);

                return data != null;
            }
        }
        public override int Priority => -1000;

        public override void OnInitilize(GameEngine engine)
        {
            engine.Assets.AddFileSource(new ShaderLibSource());
            
        }
    }
}