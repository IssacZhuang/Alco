using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
                if (!TryGetLoader(filename, out IAssetLoader<TAsset>? loader))
                {
                    asset = null;
                    failedReason = $"No asset loader found for the file '{filename}' to type {typeof(TAsset).Name}";
                    return false;
                }

                // // profile
                // EndProfile();
                if (!TryLoadAssetCore(filename, handle, loader, out TAsset? newAsset, out failedReason))
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
        throw new AssetLoadException(failedReason);
    }


    /// <summary>
    /// Load asset file and preprocess the asset asynchronously, then call the onComplete action on the main thread.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset.</typeparam>
    /// <param name="filename">Path and name of the asset file.</param>
    /// <param name="onComplete">The callback action when the asset is loaded.</param>
    /// <param name="cacheMode">Whether to cache the asset.</param>
    public void LoadAsync<TAsset>(string filename, Action<TAsset, Exception?> onComplete, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
    {

        filename = ParseEntry(filename);

        AssetHandle handle = GetAssetHandle(filename);
        lock (handle)
        {
            //try load from cache
            object? cachedAsset = handle.CachedAsset;
            if (cachedAsset is TAsset cached)
            {
                onComplete(cached, null);
                return;
            }

            // check the asset loader
            handle.OnLoadComplete += (asset, exception) => onComplete((TAsset)asset, exception);

            if (handle.IsLoading)
            {
                return;
            }

            handle.IsLoading = true;

            if (!TryGetLoader(filename, out IAssetLoader<TAsset>? assetLoaderT))
            {
                string failedReason = $"No asset loader found for the file '{filename}' to type {typeof(TAsset).Name}";
                Log.Error(failedReason);
                onComplete(null!, new AssetLoadException(failedReason));
                return;
            }

            AsyncPreprocessJob job = new AsyncPreprocessJob()
            {
                name = filename,
                onCreate = GetOnCreateAction<TAsset>(filename, cacheMode), // on worker thread
                handle = handle,
                cacheMode = cacheMode
            };

            PushJob(job);

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
    private Func<object?> GetOnCreateAction<TAsset>(string filename, AssetCacheMode cacheMode) where TAsset : class
    {
        return () =>
        {
            return Load<TAsset>(filename, cacheMode);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void PushJob(AsyncPreprocessJob job)
    {
        _lockJobQueue.Lock();
        _asyncLoadQueue.Push(job);
        _lockJobQueue.Unlock();
    }

    // Only called from the GameEngine class
    internal void OnUpdate()
    {
        int count = 0;
        //allow cas failed in serveral times
        while (count < FetchJobAttempCount)
        {
            StealingResult result = _asyncLoadQueue.TryGetFinishedTask(out AsyncPreprocessJob job, out Exception? exception);
            if (result == StealingResult.Empty)
            {
                return;
            }
            else if (result == StealingResult.Success)
            {
                HanleFinishedJob(job, exception);
            }
            else
            {
                //cas failed
                count++;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryLoadAssetCore<TAsset>(string filename, AssetHandle handle, IAssetLoader<TAsset> loader, [NotNullWhen(true)] out TAsset? asset, [NotNullWhen(false)] out string? failedReason) where TAsset : class
    {
        // assume the handle is locked
        // profile
        StartProfile<TAsset>(filename);

        // IO
        if (!TryLoadDataFromSource(filename, out ReadOnlySpan<byte> data))
        {
            failedReason = $"Trying to get asset {filename} but the file does not exist";
            asset = null;
            return false;
        }

        // create the asset
        if (!loader.TryCreateAsset(filename, data, out asset))
        {
            failedReason = $"Trying to get asset {filename} but the asset loader failed to load the asset";
            asset = null;
            return false;
        }

        // profile
        EndProfile();
        failedReason = string.Empty;
        return true;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HanleFinishedJob(AsyncPreprocessJob job, Exception? exception)
    {
        AssetHandle handle = job.handle;

        if (exception != null)
        {
            Log.Error($"Exception on creating asset '{job.name}': {exception}");
        }

        object? asset = handle.tmpAsset;
        if (asset == null)
        {
            Log.Error($"Failed to load asset: {job.name}");
        }

        lock (handle)
        {

            try
            {
                handle.DoLoadComplete(asset!, exception);
            }
            catch (Exception e)
            {
                Log.Error($"Exception on creating asset '{job.name}': {e}");
            }

            // already cached in async job
            // if (asset != null)
            // {
            //     handle.SetCache(asset, job.cacheMode);
            // }
            handle.ResetLoadingState();
        }
    }

}