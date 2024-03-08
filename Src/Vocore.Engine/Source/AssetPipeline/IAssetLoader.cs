using System;
using System.Diagnostics.CodeAnalysis;

namespace Vocore.Engine;

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

public interface IAssetLoader<TAsset> : IBaseAssetLoader where TAsset : class
{
    bool TryAsyncPreprocess(string filename, byte[] data, out object? preprocessed);
    /// <summary>
    /// Load the asset from the file
    /// </summary>
    bool TryLoad(string filename, object? preprocessed, [NotNullWhen(true)]out TAsset? asset);
}


