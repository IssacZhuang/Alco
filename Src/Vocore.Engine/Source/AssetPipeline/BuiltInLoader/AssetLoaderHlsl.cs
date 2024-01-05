using System;
using System.Text;

namespace Vocore.Engine
{
    public class AssetLoaderHlsl : IAssetLoader<Shader>
    {
        private class ShaderLibSource : IFileSource
        {
            public int Order => 0;
            public IEnumerable<string> AllFileNames => throw new NotImplementedException();

            public bool TryGetData(string path, out byte[] data)
            {
                return GameEngine.Instance.Assets.TryLoad(path, out data);
            }
        }
        private ShaderCompiler? _shaderComplier;

        public ShaderCompiler ShaderComplier
        {
            get
            {
                if (_shaderComplier == null)
                {
                    _shaderComplier = new ShaderCompiler(GameEngine.Instance.GraphicsDevice, new ShaderLibSource());
                }
                return _shaderComplier;
            }
        }
        public string Name => "HLSL Shader Loader";
        public IEnumerable<string> FileExtensions => new[] { ".hlsl" };


        public bool TryLoad(string filename, byte[] data, out Shader asset)
        {
            asset = ShaderComplier.Complie(new ShaderCompileDescription(Encoding.UTF8.GetString(data), filename));
            return true;
        }
    }
}