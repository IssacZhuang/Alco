using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// Register the asset loader to the asset manager
    /// </summary>
    /// <param name="assetLoader">The asset loader to register</param>
    /// <exception cref="Exception">Thrown when the asset loader for the extension already exists</exception>
    public void RegisterAssetLoader(IAssetLoader assetLoader)
    {
        foreach (var extension in assetLoader.FileExtensions)
        {
            if (_assetLoaders.TryGetValue(extension, out var loader))
            {
                throw new Exception($"The asset loader for extension {extension} already exists: {loader.Name}");
            }
            _assetLoaders.Add(extension, assetLoader);
        }

        _isRecongizedExtensionsDirty = true;
        _isEntryDirty = true;
    }

    /// <summary>
    /// Unregister the asset loader from the asset manager
    /// </summary>
    /// <param name="assetLoader">The asset loader to unregister</param>
    public void UnregisterAssetLoader(IAssetLoader assetLoader)
    {
        foreach (var extension in assetLoader.FileExtensions)
        {
            if (_assetLoaders.TryGetValue(extension, out var loader))
            {
                if (loader == assetLoader)
                {
                    _assetLoaders.Remove(extension);
                }
            }
        }
        _isRecongizedExtensionsDirty = true;
        _isEntryDirty = true;
    }

    private bool TryGetLoader<TAsset>(string filename, [NotNullWhen(true)] out IAssetLoader? loader)
    {
        string extension = Path.GetExtension(filename);
        if (!_assetLoaders.TryGetValue(extension, out IAssetLoader? assetLoader))
        {
            _host.LogError($"Trying to get asset {filename} but the asset loader does not exist");
            loader = null;
            return false;
        }

        if (!assetLoader.CanHandleType(typeof(TAsset)))
        {
            _host.LogError($"Trying to get asset {filename} with type {typeof(TAsset).Name} but the asset loader does not support this type");
            loader = null;
            return false;
        }

        loader = assetLoader;
        return true;
    }

    public bool IsRecongizedExtension(string extension)
    {
        UpdateRecongizedExtensions();
        return _recongizedExtensions.Contains(extension);
    }
}