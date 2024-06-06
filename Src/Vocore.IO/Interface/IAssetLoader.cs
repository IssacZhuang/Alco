using System;
using System.Diagnostics.CodeAnalysis;

namespace Vocore.IO;


/// <summary>
/// Represents an asset loader for loading and preprocessing assets of type TAsset.<br/>
/// </summary>
/// <typeparam name="TAsset">The type of asset to load.</typeparam>
public interface IAssetLoader<TAsset> : IBaseAssetHandler where TAsset : class
{
    /// <summary>
    /// Asynchronously preprocesses the asset data.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="data">The raw data of the asset.</param>
    /// <param name="preprocessed">The preprocessed asset object.</param>
    /// <returns><c>true</c> if the preprocessing is successful; otherwise, <c>false</c>.</returns>
    bool TryAsyncPreprocess(string filename, ReadOnlySpan<byte> data, [NotNullWhen(true)] out object? preprocessed);

    /// <summary>
    /// Loads the asset from the file.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="preprocessed">The preprocessed asset object.</param>
    /// <param name="asset">The loaded asset object.</param>
    /// <returns><c>true</c> if the loading is successful; otherwise, <c>false</c>.</returns>
    bool TryCreateAsset(string filename, object preprocessed, [NotNullWhen(true)] out TAsset? asset);
}


