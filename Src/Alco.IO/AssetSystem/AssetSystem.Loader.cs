using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// Get all registered asset loaders
    /// </summary>
    public IReadOnlyCollection<IAssetLoader> AllAssetLoaders => _assetLoaders.Values;

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

        // Invalidate type extension cache since new loader is registered
        _typeExtensionLookup.Clear();
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

        // Invalidate type extension cache since loader is unregistered
        _typeExtensionLookup.Clear();
    }

    private bool TryGetLoader(string filename, Type type, [NotNullWhen(true)] out IAssetLoader? loader)
    {
        //lower gc allocation a little bit
        ReadOnlySpan<char> extension = Path.GetExtension(filename.AsSpan());
        Span<char> lowerExtension = stackalloc char[extension.Length];
        extension.ToLower(lowerExtension, null);
        if (!_assetLoaders.TryGetValue(lowerExtension.ToString(), out IAssetLoader? assetLoader))
        {
            _host.LogError($"Trying to get asset {filename} but the asset loader does not exist");
            loader = null;
            return false;
        }

        if (!assetLoader.CanHandleType(type))
        {
            _host.LogError($"Trying to get asset {filename} with type {type.Name} but the asset loader does not support this type");
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