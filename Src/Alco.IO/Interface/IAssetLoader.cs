using System;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;


/// <summary>
/// Represents an asset loader for loading and preprocessing assets of type TAsset.<br/>
/// </summary>
/// <typeparam name="TAsset">The type of asset to load.</typeparam>
public interface IAssetLoader<TAsset> : IBaseAssetHandler where TAsset : class
{
    /// <summary>
    /// Creates an asset from the given filename and data.
    /// It might throw an exception if the asset cannot be created.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="data">The data of the asset.</param>
    /// <returns>The created asset.</returns>
    TAsset CreateAsset(string filename, ReadOnlySpan<byte> data);
}


