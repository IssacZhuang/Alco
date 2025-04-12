namespace Alco.IO;

/// <summary>
/// Represents information about an asset in the asset system.
/// Provides methods for loading assets synchronously and asynchronously.
/// </summary>
public readonly struct AssetInfo
{
    /// <summary>
    /// The asset system that manages this asset.
    /// </summary>
    public readonly AssetSystem AssetSystem;

    /// <summary>
    /// The path to the asset within the asset system.
    /// </summary>
    public readonly string Path;

    /// <summary>
    /// Gets a value indicating whether the asset exists in the asset system.
    /// </summary>
    public bool IsExist => AssetSystem.IsFileExist(Path);

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetInfo"/> struct.
    /// </summary>
    /// <param name="assetSystem">The asset system that manages this asset.</param>
    /// <param name="path">The path to the asset within the asset system.</param>
    public AssetInfo(AssetSystem assetSystem, string path)
    {
        AssetSystem = assetSystem;
        Path = path;
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Loads the asset as the specified type.
    /// </summary>
    /// <param name="type">The type to load the asset as.</param>
    /// <param name="cacheMode">The cache mode to use when loading the asset. Default is Recyclable.</param>
    /// <returns>The loaded asset as an object.</returns>
    public object Load(Type type, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        return AssetSystem.Load(Path, type, cacheMode);
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Loads the asset as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to load the asset as.</typeparam>
    /// <param name="cacheMode">The cache mode to use when loading the asset. Default is Recyclable.</param>
    /// <returns>The loaded asset as type T.</returns>
    public T Load<T>(AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where T : class
    {
        return AssetSystem.Load<T>(Path, cacheMode);
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Loads the asset asynchronously as the specified type and returns a Task.
    /// </summary>
    /// <param name="type">The type to load the asset as.</param>
    /// <param name="cacheMode">The cache mode to use when loading the asset. Default is Recyclable.</param>
    /// <returns>A Task that represents the asynchronous load operation. The task result contains the loaded asset as an object.</returns>
    public Task<object> LoadAsync(Type type, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        return AssetSystem.LoadAsync(Path, type, cacheMode);
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Loads the asset asynchronously as the specified type and returns a Task.
    /// </summary>
    /// <typeparam name="T">The type to load the asset as.</typeparam>
    /// <param name="cacheMode">The cache mode to use when loading the asset. Default is Recyclable.</param>
    /// <returns>A Task that represents the asynchronous load operation. The task result contains the loaded asset as type T.</returns>
    public Task<T> LoadAsync<T>(AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where T : class
    {
        return AssetSystem.LoadAsync<T>(Path, cacheMode);
    }
}
