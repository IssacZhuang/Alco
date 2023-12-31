using System;
using System.Text;

namespace Vocore.Engine
{
    public class AssetLoaderHlslInclude : IAssetLoader<byte[]>
    {
        public string Name => "HLSL Include Loader";

        public IEnumerable<string> FileExtensions => new[] { ".hlsli" };

        public bool TryLoad(string filename, byte[] data, out byte[] asset)
        {
            asset = data;
            return true;
        }
    }
}