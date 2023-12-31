using System;
using System.Text;

namespace Vocore.Engine
{
    public class AssetLoaderHlsl : IAssetLoader<Shader>
    {
        private class ShaderLibSource : IFileSource
        {
            public IEnumerable<string> AllFileNames => throw new NotImplementedException();

            public bool TryGetData(string path, out byte[] data)
            {
                return GameEngine.Instance.Assets.TryLoad(path, out data);
            }
        }
        private ShaderComplier? _shaderComplier;

        public ShaderComplier ShaderComplier
        {
            get
            {
                if (_shaderComplier == null)
                {
                    _shaderComplier = new ShaderComplier(GameEngine.Instance.GraphicsDevice, new ShaderLibSource());
                }
                return _shaderComplier;
            }
        }
        public string Name => "HLSL Shader Loader";
        public IEnumerable<string> FileExtensions => new[] { ".hlsl" };


        public bool TryLoad(string filename, byte[] data, out Shader asset)
        {
            asset = ShaderComplier.Complie(new ShaderComplieDescription(Encoding.UTF8.GetString(data), filename));
            return true;
        }
    }
}