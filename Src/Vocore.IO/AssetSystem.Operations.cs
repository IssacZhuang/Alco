using System.Diagnostics.CodeAnalysis;

namespace Vocore.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// Tries to load an asset of type <typeparamref name="TAsset"/> from the specified filename.
    /// </summary>
    /// <typeparam name="TAsset">The type of the asset to load.</typeparam>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful; otherwise, <c>null</c>.</param>
    /// <param name="failedReason">When this method returns, contains the reason why the asset failed to load if unsuccessful; otherwise, <c>null</c>.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    /// <returns><c>true</c> if the asset was successfully loaded; otherwise, <c>false</c>.</returns>
    public bool TryLoad<TAsset>(string filename, [NotNullWhen(true)] out TAsset? asset, [NotNullWhen(false)] out string? failedReason, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
    {
        TryRefreshEntries();
        filename = ParseEntry(filename);

        failedReason = string.Empty;
        try
        {
            AssetHandle handle = GetAssetHandle(filename);
            //try load from cache
            lock (handle)
            {
                object? cachedAsset = handle.CachedAsset;
                if (cachedAsset is TAsset cached)
                {
                    asset = cached;
                    return true;
                }

                handle.IsLoading = true;

                // check the asset loader
                if (!TryGetLoader(filename, out IAssetLoader<TAsset>? assetLoaderT))
                {
                    asset = null;
                    failedReason = $"No asset loader found for the file '{filename}' to type {typeof(TAsset).Name}";
                    return false;
                }

                // IO
                if (!TryLoadDataFromSource(filename, out ReadOnlySpan<byte> data))
                {
                    failedReason = $"Trying to get asset {filename} but the file does not exist";
                    asset = null;
                    return false;
                }

                // create the asset
                if (!assetLoaderT.TryCreateAsset(filename, data, out TAsset? newAsset))
                {
                    failedReason = $"Trying to get asset {filename} but the asset loader failed to load the asset";
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
    /// Tries to load an asset of type <typeparamref name="TAsset"/> from the specified filename.
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
    /// Load an asset of type <typeparamref name="TAsset"/> from the specified filename.
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
        throw new Exception(failedReason);
    }


    /// <summary>
    /// Load asset file and preprocess the asset asynchronously, then call the onComplete action on the main thread.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset.</typeparam>
    /// <param name="filename">Path and name of the asset file.</param>
    /// <param name="onComplete">The callback action when the asset is loaded.</param>
    /// <param name="cacheMode">Whether to cache the asset.</param>
    public void LoadAsync<TAsset>(string filename, Action<TAsset> onComplete, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
    {

        TryRefreshEntries();
        filename = ParseEntry(filename);

        AssetHandle handle = GetAssetHandle(filename);
        lock (handle)
        {


            //try load from cache
            object? cachedAsset = handle.CachedAsset;
            if (cachedAsset is TAsset cached)
            {
                onComplete(cached);
                return;
            }

            // check the asset loader
            if (!TryGetLoader(filename, out IAssetLoader<TAsset>? assetLoaderT))
            {
                Log.Error($"No asset loader found for the file '{filename}' to type {typeof(TAsset).Name}");
                return;
            }

            handle.OnLoadComplete += (asset) => onComplete((TAsset)asset);

            if (handle.IsLoading)
            {
                return;
            }

            AsyncPreprocessJob job = new AsyncPreprocessJob()
            {
                name = filename,
                onCreate = GetOnCreateAction(filename, assetLoaderT), // on worker thread
                handle = handle,
                cacheMode = cacheMode
            };

            _asyncLoadQueue.Push(job);
        }

    }

    /// <summary>
    /// Try to load the raw data of the asset from the file source.
    /// </summary>
    /// <param name="filename">The filename of the asset.</param>
    /// <param name="data">The raw data of the asset if it is loaded successfully; otherwise, <c>null</c>.</param>
    /// <returns><c>True</c> if the asset is loaded successfully.</returns>
    public bool TryLoadRaw(string filename, [NotNullWhen(true)] out ReadOnlySpan<byte> data)
    {

        TryRefreshEntries();

        filename = ParseEntry(filename);

        if (TryLoadDataFromSource(filename, out data))
        {
            return true;
        }

        Log.Error($"Trying to get asset {filename} but the file does not exist");
        data = default;
        return false;
    }


    //on worker thread
    private Func<object?> GetOnCreateAction<TAsset>(string filename, IAssetLoader<TAsset> assetLoaderT) where TAsset : class
    {
        return () =>
        {
            if (!TryLoadDataFromSource(filename, out ReadOnlySpan<byte> data))
            {
                Log.Error($"Trying to get asset {filename} but the file does not exist");
                return null;
            }

            if (!assetLoaderT.TryCreateAsset(filename, data, out TAsset? newAsset))
            {
                Log.Error($"Trying to get asset {filename} but the asset loader failed to load the asset");
                return null;
            }

            return newAsset;
        };
    }

    private bool TryLoadDataFromSource(string filename, out ReadOnlySpan<byte> data)
    {
        if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
        {
            if (fileSource.TryGetData(filename, out data))
            {
                return true;
            }
        }
        data = default;
        return false;
    }

    private AssetHandle GetAssetHandle(string filename)
    {
        if (_assetLookup.TryGetValue(filename, out AssetHandle? handle))
        {
            return handle;
        }

        AssetHandle newHandle = new AssetHandle();
        if (_assetLookup.TryAdd(filename, newHandle))
        {
            return newHandle;
        }
        //otherwise handle is added by another thread

        return _assetLookup[filename];

    }

    // Only called from the GameEngine class
    internal void OnUpdate()
    {
        for (int i = 0; i < FetchFinishJobAttempCount; i++)
        {
            StealingResult result = _asyncLoadQueue.TryGetFinishedTask(out AsyncPreprocessJob job, out Exception? exception);
            if (result == StealingResult.Empty)
            {
                return;
            }
            else if (result == StealingResult.Success)
            {
                AssetHandle handle = job.handle;
                if (exception != null)
                {
                    Log.Error($"Exception on loading asset '{job.name}': {exception}");

                    lock (handle)
                    {
                        handle.ResetLoadingState();
                    }
                    continue;
                }

                if (job.asset == null)
                {
                    Log.Error($"The preprocessed asset of '{job.name}' is null, the asset manager failed to load the asset");
                    lock (handle)
                    {
                        handle.ResetLoadingState();
                    }
                    continue;
                }

                object? asset = job.asset;
                if (asset == null)
                {
                    Log.Error($"Failed to create asset: {job.name}");
                    lock (handle)
                    {
                        handle.ResetLoadingState();
                    }
                    continue;
                }

                lock (handle)
                {
                    try
                    {
                        handle.DoLoadComplete(asset);
                    }
                    catch (Exception e)
                    {
                        Log.Error($"Exception on creating asset '{job.name}': {e}");
                    }
                    handle.SetCache(job.asset, job.cacheMode);
                    handle.ResetLoadingState();
                }
            }


        }
    }

}