using System;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;


/// <summary>
/// Represents an asset loader for loading and preprocessing assets of type TAsset.<br/>
/// </summary>
/// <typeparam name="TAsset">The type of asset to load.</typeparam>
public interface IAssetLoader
{
    /// <summary>
    /// The name of the asset loader
    /// </summary>
    string Name { get; }
    /// <summary>
    /// The file extension of the asset loader
    /// </summary>
    IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Check if the asset loader can handle the type.
    /// </summary>
    /// <param name="type">The type of the asset.</param>
    /// <returns>True if the asset loader can handle the type, false otherwise.</returns>
    bool CanHandleType(Type type);

    /// <summary>
    /// Creates an asset from the given filename and data.
    /// It might throw an exception if the asset cannot be created.
    /// 
    /// <br/> [note] Just throw an exception if the asset cannot be created when implementing this method.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="data">The data of the asset.</param>
    /// <param name="targetType">The type of the asset.</param>
    /// <returns>The created asset.</returns>
    object CreateAsset(string filename, ReadOnlySpan<byte> data, Type targetType);
}


