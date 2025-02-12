using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    private HttpClient? _httpClient;
    //lazy init
    private HttpClient HttpClient => _httpClient ??= new HttpClient();


    /// <summary>
    /// Attempts to load a remote asset from a URL with error handling and caching support.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset to load. Must be a reference type.</typeparam>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful, or null if loading failed.</param>
    /// <param name="failedReason">When this method returns, contains the error message if loading failed, or null if successful.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>true if the asset was loaded successfully; otherwise, false.</returns>
    /// <remarks>
    /// This method handles concurrent loading attempts through locking mechanisms and supports caching of loaded assets.
    /// If the asset is already cached, it will be returned from the cache instead of downloading again.
    /// </remarks>
    public bool TryLoadRemote<TAsset>(string url, [NotNullWhen(true)] out TAsset? asset, [NotNullWhen(false)] out string? failedReason, AssetCacheMode cacheMode = AssetCacheMode.None) where TAsset : class
    {
        asset = null;
        failedReason = string.Empty;

        if (string.IsNullOrEmpty(url))
        {
            failedReason = "URL cannot be null or empty";
            return false;
        }

        string extension = Path.GetExtension(url);
        AssetHandle handle = GetAssetHandle(url);

        try
        {
            lock (handle)
            {
                //it might be loaded by other thread, try load from cache
                object? cachedAsset = handle.CachedAsset;
                if (cachedAsset is TAsset cached)
                {
                    asset = cached;
                    return true;
                }

                handle.IsLoading = true;

                // Get appropriate loader
                if (!TryGetLoader(extension, out IAssetLoader<TAsset>? loader))
                {
                    handle.IsLoading = false;
                    failedReason = $"No asset loader found for the URL '{url}' to type {typeof(TAsset).Name}";
                    return false;
                }

                StartProfile<TAsset>(url);

                // Download the data
                byte[] data;
                try
                {
                    data = HttpClient.GetByteArrayAsync(url).Result;
                }
                catch (Exception ex)

                {
                    handle.IsLoading = false;
                    EndProfile(false);
                    failedReason = $"Failed to download remote asset '{url}': {ex.Message}";
                    return false;
                }

                var safeMemory = new SafeMemoryHandle(data);

                // Create the asset
                try
                {
                    asset = loader.CreateAsset(url, safeMemory.Span);
                    safeMemory.Dispose();
                }
                catch (Exception ex)
                {
                    handle.IsLoading = false;
                    EndProfile(false);
                    failedReason = $"Exception occurred while creating asset {url}: {ex}";
                    return false;
                }

                // Cache the result
                handle.SetCache(asset, cacheMode);
                handle.IsLoading = false;

                EndProfile();

                return true;
            }
        }
        catch (Exception ex)
        {
            failedReason = $"Failed to load remote asset '{url}': {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Attempts to load a remote asset from a URL with simplified error handling.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset to load. Must be a reference type.</typeparam>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful, or null if loading failed.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>true if the asset was loaded successfully; otherwise, false.</returns>
    public bool TryLoadRemote<TAsset>(string url, [NotNullWhen(true)] out TAsset? asset, AssetCacheMode cacheMode = AssetCacheMode.None) where TAsset : class
    {
        return TryLoadRemote(url, out asset, out _, cacheMode);
    }

    /// <summary>
    /// Loads a remote asset from a URL and throws an exception if loading fails.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset to load. Must be a reference type.</typeparam>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>The loaded asset.</returns>
    /// <exception cref="AssetLoadException">Thrown when the asset cannot be loaded.</exception>
    public TAsset LoadRemote<TAsset>(string url, AssetCacheMode cacheMode = AssetCacheMode.None) where TAsset : class
    {
        if (TryLoadRemote(url, out TAsset? asset, out string? failedReason, cacheMode))
        {
            return asset;
        }
        throw new AssetLoadException(failedReason);
    }

    /// <summary>
    /// Asynchronously loads a remote asset from a URL.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset to load. Must be a reference type.</typeparam>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>A task that represents the asynchronous load operation. The task result contains the loaded asset.</returns>
    /// <exception cref="AssetLoadException">Thrown when the asset cannot be loaded.</exception>
    public Task<TAsset> LoadRemoteAsync<TAsset>(string url, AssetCacheMode cacheMode = AssetCacheMode.None) where TAsset : class
    {
        return Task.Run(() => LoadRemote<TAsset>(url, cacheMode));
    }
}
