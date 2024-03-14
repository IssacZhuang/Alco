using System.Diagnostics.CodeAnalysis;

namespace Vocore.Engine;

/// <summary>
/// Represents an interface for hot reloading assets.
/// </summary>
/// <typeparam name="TAsset">The type of the asset.</typeparam>
public interface IAssetHotReloader<TAsset> where TAsset : class
{
    /// <summary>
    /// Asynchronously preprocesses the asset data.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="data">The raw data of the asset.</param>
    /// <param name="preprocessed">The preprocessed asset object.</param>
    /// <returns><c>true</c> if the preprocessing is successful; otherwise, <c>false</c>.</returns>
    bool TryAsyncPreprocess(string filename, byte[] data, [NotNullWhen(true)] out object? preprocessed);


    /// <summary>
    /// Attempts to hot reload the specified asset without destroying the existing asset.
    /// </summary>
    /// <param name="asset">The asset to hot reload.</param>
    /// <param name="preprocessed">The preprocessed data for the asset.</param>
    /// <returns><c>true</c> if the hot reload was successful; otherwise, <c>false</c>.</returns>
    bool TryHotReload(TAsset asset, object preprocessed);
}
