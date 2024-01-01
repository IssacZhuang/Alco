using System;

namespace Vocore.Engine
{
    public interface IBaseAssetLoader
    {
        /// <summary>
        /// The name of the asset loader
        /// </summary>
        string Name { get; }
        /// <summary>
        /// The file extension of the asset loader
        /// </summary>
        IEnumerable<string> FileExtensions { get; }
    }
    public interface IAssetLoader<TAsset> :IBaseAssetLoader where TAsset : class
    {
        /// <summary>
        /// Load the asset from the file
        /// </summary>
        bool TryLoad(string filename, byte[] data, out TAsset asset);
    }
}
