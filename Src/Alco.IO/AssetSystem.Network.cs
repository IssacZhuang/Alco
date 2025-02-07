using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    private static readonly HttpClient _httpClient = new HttpClient();

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
                    data = _httpClient.GetByteArrayAsync(url).Result;
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

    public bool TryLoadRemote<TAsset>(string url, [NotNullWhen(true)] out TAsset? asset, AssetCacheMode cacheMode = AssetCacheMode.None) where TAsset : class
    {
        return TryLoadRemote(url, out asset, out _, cacheMode);
    }

    public TAsset LoadRemote<TAsset>(string url, AssetCacheMode cacheMode = AssetCacheMode.None) where TAsset : class
    {
        if (TryLoadRemote(url, out TAsset? asset, out string? failedReason, cacheMode))
        {
            return asset;
        }
        throw new AssetLoadException(failedReason);
    }

    public Task<TAsset> LoadRemoteAsync<TAsset>(string url, AssetCacheMode cacheMode = AssetCacheMode.None) where TAsset : class
    {
        return Task.Run(() => LoadRemote<TAsset>(url, cacheMode));
    }
}
