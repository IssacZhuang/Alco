using System;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;


/// <summary>
/// Represents an asset loader for loading and preprocessing assets of type TAsset.
/// <br/>[Note]All implementations must be thread-safe.
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
    /// Creates an asset from the given asset load context.
    /// It might throw an exception if the asset cannot be created.
    /// 
    /// <br/> [note] Just throw an exception if the asset cannot be created when implementing this method.
    /// </summary>
    /// <param name="context">The asset load context containing filename, data, and target type.</param>
    /// <returns>The created asset.</returns>
    object CreateAsset(in AssetLoadContext context);
}


