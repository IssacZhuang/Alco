namespace Alco.IO;

/// <summary>
/// The single type asset loader
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class BaseAssetLoader<T> : IAssetLoader
{

    /// <summary>Gets the loader name.</summary>
    public abstract string Name { get; }

    /// <summary>Gets the file extensions this loader handles.</summary>
    public abstract IReadOnlyList<string> FileExtensions { get; }

    /// <summary>
    /// Returns whether this loader creates assets of the given type.
    /// </summary>
    /// <param name="type">The requested asset type.</param>
    /// <returns>True when <paramref name="type"/> is <typeparamref name="T"/>; otherwise false.</returns>
    public virtual bool CanHandleType(Type type)
    {
        return type == typeof(T);
    }

    /// <summary>
    /// Creates the asset from the given load context.
    /// </summary>
    /// <param name="context">The asset load context.</param>
    /// <returns>The created asset.</returns>
    public abstract object CreateAsset(in AssetLoadContext context);
}

