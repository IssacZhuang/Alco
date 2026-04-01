using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// Tries to load an asset of type <typeparamref name="TAsset"/> from the specified filename.
    /// <br/>This method is thread safe.
    /// </summary>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful; otherwise, <c>null</c>.</param>
    /// <param name="failedReason">When this method returns, contains the reason why the asset failed to load if unsuccessful; otherwise, <c>null</c>.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns><c>true</c> if the asset was successfully loaded; otherwise, <c>false</c>.</returns>
    public bool TryLoad(string filename, Type type, [NotNullWhen(true)] out object? asset, [NotNullWhen(false)] out string failedReason, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        TryRefreshEntries();
        filename = ParseEntry(filename);

        // the real filename if the filename is an alias
        if (!IsFileExist(filename, out string realFilename))
        {
            failedReason = $"The file '{filename}' does not exist";
            asset = null;
            return false;
        }

        filename = realFilename;

        failedReason = string.Empty;
        try
        {
            AssetHandle handle = GetAssetHandle(filename);
            //try load from cache
            lock (handle)
            {
                object? cachedAsset = handle.CachedAsset;
                if (cachedAsset != null && type.IsInstanceOfType(cachedAsset))
                {
                    asset = cachedAsset;
                    return true;
                }

                handle.IsLoading = true;



                // check the asset loader
                if (!TryGetLoader(filename, type, out IAssetLoader? loader))
                {
                    asset = null;
                    failedReason = $"No asset loader found for the file '{filename}' to type {type.Name}";
                    return false;
                }

                // // profile
                // EndProfile();
                if (!TryLoadAssetCore(filename, type, handle, loader, out object? newAsset, out failedReason))
                {
                    asset = null;
                    return false;
                }

                // try add to cache
                handle.SetCache(newAsset, cacheMode);

                handle.IsLoading = false;
                asset = newAsset;

                return true;
            }
        }
        catch (Exception ex)
        {
            failedReason = $"Exception on loading asset '{filename}': {ex}";
            asset = null;
            return false;
        }
    }

    /// <summary>
    /// Tries to load an asset from the specified filename. The type of the asset is determined by the type parameter.
    /// <br/>This method is thread safe.
    /// </summary>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="type">The type of the asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful; otherwise, <c>null</c>.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns><c>true</c> if the asset was successfully loaded; otherwise, <c>false</c>.</returns>
    public bool TryLoad(string filename, Type type, [NotNullWhen(true)] out object? asset, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        return TryLoad(filename, type, out asset, out _, cacheMode);
    }

    /// <summary>
    /// Tries to load an asset of type <typeparamref name="TAsset"/> from the specified filename.
    /// <br/>This method is thread safe.
    /// </summary>
    /// <typeparam name="TAsset">The type of the asset to load.</typeparam>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful; otherwise, <c>null</c>.</param>
    /// <param name="failedReason">When this method returns, contains the reason why the asset failed to load if unsuccessful; otherwise, <c>null</c>.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns></returns>
    public bool TryLoad<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset, [NotNullWhen(false)] out string failedReason, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        bool result = TryLoad(filename, typeof(TAsset), out object? assetObject, out failedReason, cacheMode);
        if (result)
        {
            asset = (TAsset)assetObject!;
            return true;
        }
        asset = default;
        return false;
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Tries to load an asset of type <typeparamref name="TAsset"/> from the specified filename.
    /// </summary>
    /// <typeparam name="TAsset">The type of the asset to load.</typeparam>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful; otherwise, <c>null</c>.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns><c>true</c> if the asset was successfully loaded; otherwise, <c>false</c>.</returns>
    public bool TryLoad<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
    {
        return TryLoad(filename, out asset, out _, cacheMode);
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Load an asset of type <typeparamref name="TAsset"/> from the specified filename.
    /// </summary>
    /// <typeparam name="TAsset">The type of the asset to load.</typeparam>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns>The loaded asset.</returns>
    /// <exception cref="Exception">Thrown when the asset failed to load.</exception>
    public TAsset Load<TAsset>(string filename, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
    {
        if (TryLoad(filename, out TAsset? asset, out string? failedReason, cacheMode))
        {
            return asset;
        }
        throw new AssetLoadException(failedReason);
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Load an asset of type <paramref name="type"/> from the specified filename.
    /// </summary>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="type">The type of the asset to load.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns>The loaded asset.</returns>
    /// <exception cref="AssetLoadException">Thrown when the asset failed to load.</exception>
    public object Load(string filename, Type type, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        if (TryLoad(filename, type, out object? asset, out string? failedReason, cacheMode))
        {
            return asset;
        }
        throw new AssetLoadException(failedReason);
    }


    /// <summary>
    /// <c>[Thread Safe]</c> Load asset file and preprocess the asset asynchronously, then return the asset as a task.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset.</typeparam>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns>The loaded asset as a task.</returns>
    public Task<TAsset> LoadAsync<TAsset>(string filename, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
    {
        return Task.Run(() => Load<TAsset>(filename, cacheMode));
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Load asset file and preprocess the asset asynchronously, then return the asset as a task.
    /// </summary>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="type">The type of the asset to load.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns>The loaded asset as a task.</returns>
    public Task<object> LoadAsync(string filename, Type type, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        return Task.Run(() => Load(filename, type, cacheMode));
    }


    /// <summary>
    /// <c>[Thread Safe]</c> Try to load the raw data of the asset from the file source.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="data">The raw data of the asset if it is loaded successfully; otherwise, <c>null</c>.</param>
    /// <returns><c>True</c> if the asset is loaded successfully.</returns>
    public bool TryLoadRaw(string filename, [NotNullWhen(true)] out SafeMemoryHandle data)
    {

        TryRefreshEntries();

        filename = ParseEntry(filename);

        if (TryLoadDataFromSource(filename, out data))
        {
            return true;
        }

        _host.LogError($"Trying to get asset {filename} but the file does not exist");
        data = SafeMemoryHandle.Empty;
        return false;
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Try to decode the raw data of the asset from the file source.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="type">The type of the asset.</param>
    /// <param name="data">The raw data of the asset.</param>
    /// <param name="asset">When this method returns, contains the decoded asset if successful; otherwise, <c>null</c>.</param>
    /// <param name="failedReason">When this method returns, contains the reason why the asset failed to decode if unsuccessful; otherwise, <c>null</c>.</param>
    /// <returns><c>True</c> if the asset is decoded successfully.</returns>
    public bool TryDecode(string filename, Type type, ReadOnlySpan<byte> data, [NotNullWhen(true)] out object? asset, [NotNullWhen(false)] out string failedReason)
    {
        if (TryGetLoader(filename, type, out IAssetLoader? loader))
        {
            asset = loader.CreateAsset(new AssetLoadContext(this, filename, data, type));
            failedReason = string.Empty;
            return true;
        }

        failedReason = $"No asset loader found for the file '{filename}' to type {type.Name}";
        asset = null;
        return false;
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Try to decode the raw data of the asset from the file source.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="type">The type of the asset.</param>
    /// <param name="data">The raw data of the asset.</param>
    /// <param name="asset">When this method returns, contains the decoded asset if successful; otherwise, <c>null</c>.</param>
    /// <returns><c>True</c> if the asset is decoded successfully.</returns>
    public bool TryDecode(string filename, Type type, ReadOnlySpan<byte> data, [NotNullWhen(true)] out object? asset)
    {
        return TryDecode(filename, type, data, out asset, out _);
    }

    /// <summary>
    /// Decode the raw data of the asset from the file source.
    /// <br/>This method is thread safe.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="type">The type of the asset.</param>
    /// <param name="data">The raw data of the asset.</param>
    /// <returns>The decoded asset.</returns>
    /// <exception cref="AssetLoadException">Thrown when the asset failed to decode.</exception>
    public object Decode(string filename, Type type, ReadOnlySpan<byte> data)
    {
        if (TryDecode(filename, type, data, out object? asset, out string? failedReason))
        {
            return asset;
        }
        throw new AssetLoadException(failedReason);
    }




    private bool TryLoadDataFromSource(string filename, out SafeMemoryHandle data)
    {
        if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
        {
            if (fileSource.TryGetData(filename, out data, out string? _))
            {
                return true;
            }
        }
        data = SafeMemoryHandle.Empty;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private AssetHandle GetAssetHandle(string filename)
    {
        return _assetLookup.GetOrAdd(filename, CreateAssetHandle);
        static AssetHandle CreateAssetHandle(string filename)
        {
            return new AssetHandle();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryLoadAssetCore(string filename, Type type, AssetHandle handle, IAssetLoader loader, [NotNullWhen(true)] out object? asset, [NotNullWhen(false)] out string failedReason)
    {
        // assume the handle is locked
        // profile
        using var profileScope = StartProfile(filename, type);

        // IO
        if (!TryLoadDataFromSource(filename, out SafeMemoryHandle data))
        {
            failedReason = $"Trying to get asset {filename} but the file does not exist";
            asset = null;
            profileScope?.Fail();
            return false;
        }

        try
        {
            var context = new AssetLoadContext(this, filename, data.AsReadOnlySpan(), type);
            asset = loader.CreateAsset(in context);
            data.Dispose();
        }
        catch (Exception ex)
        {
            failedReason = $"Exception occurred while creating asset {filename}: {ex}";
            asset = null;
            profileScope?.Fail();
            return false;
        }

        if (asset == null || !type.IsInstanceOfType(asset))
        {
            failedReason = $"The asset loader {loader.Name} returned an asset of type {asset?.GetType().Name} instead of {type.Name}";
            profileScope?.Fail();
            return false;
        }

        // profile
        failedReason = string.Empty;
        return true;
    }


}