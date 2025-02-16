using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    private HttpClient? _httpClient;
    //lazy init
    private HttpClient HttpClient => _httpClient ??= CreateHttpClient();

    private HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
        };
        return new HttpClient(handler);
    }

    /// <summary>
    /// Attempts to load a remote asset from a URL with error handling and caching support.
    /// </summary>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful, or null if loading failed.</param>
    /// <param name="failedReason">When this method returns, contains the error message if loading failed, or null if successful.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>true if the asset was loaded successfully; otherwise, false.</returns>
    /// <remarks>
    /// This method handles concurrent loading attempts through locking mechanisms and supports caching of loaded assets.
    /// If the asset is already cached, it will be returned from the cache instead of downloading again.
    /// </remarks>
    public bool TryLoadRemote(string url, Type type, [NotNullWhen(true)] out object? asset, [NotNullWhen(false)] out string? failedReason, AssetCacheMode cacheMode = AssetCacheMode.None)
    {
        asset = null;
        failedReason = string.Empty;

        if (string.IsNullOrEmpty(url))
        {
            failedReason = "URL cannot be null or empty";
            return false;
        }

        AssetHandle handle = GetAssetHandle(url);

        try
        {
            lock (handle)
            {
                //it might be loaded by other thread, try load from cache
                object? cachedAsset = handle.CachedAsset;
                if (cachedAsset != null && type.IsInstanceOfType(cachedAsset))
                {
                    asset = cachedAsset;
                    return true;
                }

                handle.IsLoading = true;

                StartProfile(url, type);

                // Get appropriate loader
                if (!TryGetLoader(url, type, out IAssetLoader? loader))
                {
                    failedReason = $"No asset loader found for the URL '{url}' to type {type.Name}";
                    return FailWithCleanup();
                }


                // Download the data
                byte[] data;
                try
                {
                    data = HttpClient.GetByteArrayAsync(url).Result;
                }
                catch (Exception ex)
                {
                    failedReason = $"Failed to download remote asset '{url}': {ex.Message}";
                    return FailWithCleanup();
                }

                var safeMemory = new SafeMemoryHandle(data);

                // Create the asset
                try
                {
                    asset = loader.CreateAsset(url, safeMemory.Span, type);
                    safeMemory.Dispose();
                }
                catch (Exception ex)
                {
                    failedReason = $"Exception occurred while creating asset {url}: {ex}";
                    return FailWithCleanup();
                }

                if (asset == null || !type.IsInstanceOfType(asset))
                {
                    failedReason = $"The asset loader {loader.Name} returned an asset of type {asset?.GetType().Name} instead of {type.Name}";
                    return FailWithCleanup();
                }

                // Cache the result
                handle.SetCache(asset, cacheMode);
                handle.IsLoading = false;

                try
                {
                    loader.OnAssetLoaded(asset);
                }
                catch (Exception ex)
                {
                    failedReason = $"Failed to post-process asset {url}: {ex}";
                    return FailWithCleanup();
                }

                EndProfile();

                return true;
            }
        }
        catch (Exception ex)
        {
            failedReason = $"Failed to load remote asset '{url}': {ex.Message}";
            return false;
        }

        bool FailWithCleanup()
        {
            handle.IsLoading = false;
            EndProfile(false);
            return false;
        }
    }

    /// <summary>
    /// Attempts to load a remote asset from a URL with simplified error handling.
    /// </summary>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="type">The type of the asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful, or null if loading failed.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>true if the asset was loaded successfully; otherwise, false.</returns>
    public bool TryLoadRemote(string url, Type type, [NotNullWhen(true)] out object? asset, AssetCacheMode cacheMode = AssetCacheMode.None)
    {
        return TryLoadRemote(url, type, out asset, out _, cacheMode);
    }

    /// <summary>
    /// Attempts to load a remote asset from a URL with simplified error handling.
    /// </summary>
    /// <typeparam name="TAsset">The type of asset to load. Must be a reference type.</typeparam>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="asset">When this method returns, contains the loaded asset if successful, or null if loading failed.</param>
    /// <param name="failedReason">When this method returns, contains the error message if loading failed, or null if successful.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>true if the asset was loaded successfully; otherwise, false.</returns>
    public bool TryLoadRemote<TAsset>(string url, [NotNullWhen(true)] out TAsset? asset, [NotNullWhen(false)] out string? failedReason, AssetCacheMode cacheMode = AssetCacheMode.None)
    {
        if (TryLoadRemote(url, typeof(TAsset), out object? tmpAsset, out failedReason, cacheMode))
        {
            asset = (TAsset)tmpAsset;
            return true;
        }
        asset = default;
        return false;
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
    /// Loads a remote asset from a URL and throws an exception if loading fails.
    /// </summary>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="type">The type of the asset to load.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>The loaded asset.</returns>
    /// <exception cref="AssetLoadException">Thrown when the asset cannot be loaded.</exception>
    public object LoadRemote(string url, Type type, AssetCacheMode cacheMode = AssetCacheMode.None)
    {
        if (TryLoadRemote(url, type, out object? asset, out string? failedReason, cacheMode))
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

    /// <summary>
    /// Asynchronously loads a remote asset from a URL.
    /// </summary>
    /// <param name="url">The URL of the remote asset to load.</param>
    /// <param name="type">The type of the asset to load.</param>
    /// <param name="cacheMode">Specifies how the loaded asset should be cached. Defaults to AssetCacheMode.None.</param>
    /// <returns>A task that represents the asynchronous load operation. The task result contains the loaded asset.</returns>
    /// <exception cref="AssetLoadException">Thrown when the asset cannot be loaded.</exception>
    public Task<object> LoadRemoteAsync(string url, Type type, AssetCacheMode cacheMode = AssetCacheMode.None)
    {
        return Task.Run(() => LoadRemote(url, type, cacheMode));
    }
}
