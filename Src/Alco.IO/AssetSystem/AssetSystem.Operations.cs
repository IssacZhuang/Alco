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

                //post-process
                loader.OnAssetLoaded(newAsset);
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
    /// Tries to load an asset of type <typeparamref name="TAsset"/> from the specified filename.
    /// 
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
    /// <br/>This method is thread safe.
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

    public object Load(string filename, Type type, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {
        if (TryLoad(filename, type, out object? asset, out string? failedReason, cacheMode))
        {
            return asset;
        }
        throw new AssetLoadException(failedReason);
    }


    /// <summary>
    /// Load asset file and preprocess the asset asynchronously, then return the asset as a task.
    /// <br/>This method is thread safe.
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
    /// Load asset file and preprocess the asset asynchronously, then return the asset as a task.
    /// <br/>This method is thread safe.
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
    /// Load asset file and preprocess the asset asynchronously, then call the onComplete action on the main thread.
    /// <br/>This method is thread safe.
    /// </summary>
    /// <param name="filename">Path and name of the asset file.</param>
    /// <param name="onComplete">The callback action when the asset is loaded.</param>
    /// <param name="cacheMode">Whether to cache the asset.</param>
    public void LoadAsync(string filename, Type type, Action<object, Exception?> onComplete, AssetCacheMode cacheMode = AssetCacheMode.Recyclable)
    {

        filename = ParseEntry(filename);

        AssetHandle handle = GetAssetHandle(filename);
        lock (handle)
        {
            //try load from cache
            object? cachedAsset = handle.CachedAsset;
            if (cachedAsset != null && type.IsInstanceOfType(cachedAsset))
            {
                onComplete(cachedAsset, null);
                return;
            }

            // check the asset loader
            handle.OnLoadComplete += (asset, exception) => onComplete(asset, exception);

            if (handle.IsLoading)
            {
                return;
            }

            handle.IsLoading = true;

            AsyncPreprocessJob job = new AsyncPreprocessJob()
            {
                name = filename,
                onCreate = GetOnCreateAction(filename, type, cacheMode), // on worker thread
                handle = handle,
                cacheMode = cacheMode
            };

            PushJob(job);
        }
    }

    /// <summary>
    /// Load asset file and preprocess the asset asynchronously, then call the onComplete action on the main thread.
    /// <br/>This method is thread safe.
    /// </summary>
    /// <typeparam name="TAsset">The type of the asset to load.</typeparam>
    /// <param name="filename">The filename of the asset to load.</param>
    /// <param name="onComplete">The callback action when the asset is loaded.</param>
    /// <param name="cacheMode">The cache mode for the loaded asset. Default is <see cref="AssetCacheMode.Recyclable"/>.</param>
    public void LoadAsync<TAsset>(string filename, Action<TAsset, Exception?> onComplete, AssetCacheMode cacheMode = AssetCacheMode.Recyclable) where TAsset : class
    {
        LoadAsync(filename, typeof(TAsset), (asset, exception) => onComplete((TAsset)asset, exception), cacheMode);
    }

    /// <summary>
    /// Try to load the raw data of the asset from the file source.
    /// <br/>This method is thread safe.
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


    //on worker thread
    private Func<object?> GetOnCreateAction(string filename, Type type, AssetCacheMode cacheMode)
    {
        return () =>
        {
            return Load(filename, type, cacheMode);
        };
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
    private void OnHandleAssetLoaded()
    {
        int count = 0;
        //allow cas failed in serveral times
        while (count < FetchJobAttempCount)
        {
            StealingResult result = _asyncLoadQueue.TryGetFinishedTask(out AsyncPreprocessJob job, out Exception? exception);
            if (result == StealingResult.Empty)
            {
                break;
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

        ProcessHotReloadQueue();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryLoadAssetCore(string filename, Type type, AssetHandle handle, IAssetLoader loader, [NotNullWhen(true)] out object? asset, [NotNullWhen(false)] out string failedReason)
    {
        // assume the handle is locked
        // profile
        StartProfile(filename, type);

        // IO
        if (!TryLoadDataFromSource(filename, out SafeMemoryHandle data))
        {
            failedReason = $"Trying to get asset {filename} but the file does not exist";
            asset = null;
            EndProfile(false);
            return false;
        }

        try
        {
            asset = loader.CreateAsset(filename, data.Span, type);
            data.Dispose();
        }
        catch (Exception ex)
        {
            failedReason = $"Exception occurred while creating asset {filename}: {ex}";
            asset = null;
            EndProfile(false);
            return false;
        }

        if (asset == null || !type.IsInstanceOfType(asset))
        {
            failedReason = $"The asset loader {loader.Name} returned an asset of type {asset?.GetType().Name} instead of {type.Name}";
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
            _host.LogError($"Exception on creating asset '{job.name}': {exception}");
        }

        object? asset = handle.tmpAsset;
        if (asset == null)
        {
            _host.LogError($"Failed to load asset: {job.name}");
        }

        lock (handle)
        {

            try
            {
                handle.DoLoadComplete(asset!, exception);
            }
            catch (Exception e)
            {
                _host.LogError($"Exception on creating asset '{job.name}': {e}");
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