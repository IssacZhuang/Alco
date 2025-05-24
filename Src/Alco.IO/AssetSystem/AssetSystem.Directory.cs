namespace Alco.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// Get all the file names of the assets
    /// </summary>
    public IEnumerable<string> AllAssetNames
    {
        get
        {
            TryRefreshEntries();
            return _fileEntries.Keys;
        }
    }

    /// <summary>
    /// The asset names without the extension
    /// </summary>
    public IEnumerable<string> AllAssetAliases
    {
        get
        {
            return _assetAliases.Keys;
        }
    }

    
    /// <summary>
    /// Get all the asset infos
    /// </summary>
    /// <value>All the asset infos</value>
    public IEnumerable<AssetInfo> AllAssetInfos
    {
        get
        {
            return AllAssetNames.Select(x => new AssetInfo(this, x));
        }
    }

    /// <summary>
    /// Check if the file exists
    /// /// </summary>
    /// <param name="filename">The filename to check</param>
    /// <returns>True if the file exists</returns>
    public bool IsFileExist(string? filename)
    {
        if (filename == null)
        {
            return false;
        }

        if (_assetAliases.TryGetValue(filename, out string? alias))
        {
            return _fileEntries.ContainsKey(alias);
        }
        return _fileEntries.ContainsKey(filename);
    }

    /// <summary>
    /// Add the file source to the asset manager
    /// </summary>
    /// <param name="fileSource">The file source to add</param>
    public void AddFileSource(IFileSource fileSource)
    {
        ArgumentNullException.ThrowIfNull(fileSource);
        _fileSources.Add(fileSource);
        _isEntryDirty = true;
    }

    /// <summary>
    /// Remove the file source from the asset manager
    /// </summary>
    /// <param name="fileSource">The file source to remove</param>
    public void RemoveFileSource(IFileSource fileSource)
    {
        ArgumentNullException.ThrowIfNull(fileSource);
        _fileSources.Remove(fileSource);
        _isEntryDirty = true;
    }

    /// <summary>
    /// Remove all the file sources
    /// </summary>
    public void RemoveAllFileSource()
    {
        _fileSources.Clear();
        _isEntryDirty = true;
    }

    /// <summary>
    /// Try to refresh the file entries and the recongized extensions if they are dirty
    /// </summary>
    /// <returns>True if the file entries or the recongized extensions are dirty</returns>
    public bool TryRefreshEntries()
    {
        bool result = _isEntryDirty || _isRecongizedExtensionsDirty;
        UpdateRecongizedExtensions();
        UpdateEntries();
        return result;
    }

    /// <summary>
    /// Force to refresh the file entries and the recongized extensions
    /// </summary>
    public void ForceRefreshEntries()
    {
        UpdateRecongizedExtensions(true);
        UpdateEntries(true);
    }

    /// <summary>
    /// Mark the file entries as dirty
    /// </summary>
    public void MarkEntriesDirty()
    {
        _isEntryDirty = true;
    }

    /// <summary>
    /// List all the assets in the given path
    /// </summary>
    /// <param name="path">The path to list the assets</param>
    /// <returns>The list of assets in the given path</returns>
    public IEnumerable<string> ListAssetsInPath(string path)
    {
        TryRefreshEntries();
        if (path == null || path.Equals(string.Empty))
        {
            foreach (var asset in AllAssetNames)
            {
                yield return asset;
            }
            yield break;
        }

        path = ParseEntry(path);
        if (!path.EndsWith('/'))
        {
            path += '/';
        }

        foreach (string assetPath in AllAssetNames)
        {
            if (assetPath.StartsWith(path))
            {
                yield return assetPath;
            }
        }
    }

    /// <summary>
    /// List all the assets in the given path that can be loaded as the specified type
    /// </summary>
    /// <param name="path">The path to list the assets</param>
    /// <param name="type">The type to filter assets by</param>
    /// <returns>The list of assets in the given path that can be loaded as the specified type</returns>
    public IEnumerable<string> ListAssetsInPath(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        // Get all supported extensions for the type
        IReadOnlySet<string> supportedExtensions = GetExtensionsForType(type);

        if (supportedExtensions.Count == 0)
        {
            // No loaders support this type, return empty
            yield break;
        }

        // Get all assets in the path and filter by supported extensions
        foreach (string assetPath in ListAssetsInPath(path))
        {
            string extension = Path.GetExtension(assetPath).ToLowerInvariant();
            if (supportedExtensions.Contains(extension))
            {
                yield return assetPath;
            }
        }
    }

    /// <summary>
    /// Get the file extensions that can load the specified type.
    /// This method is thread-safe and uses caching for performance.
    /// </summary>
    /// <param name="type">The type to get extensions for</param>
    /// <returns>A set of file extensions that can load the specified type</returns>
    public IReadOnlySet<string> GetExtensionsForType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return _typeExtensionLookup.AddOrUpdate(
            type,
            ComputeExtensionsForType, // addValue factory
            (keyType, existingValue) => existingValue  // updateValue factory - return existing value
        );
    }

    /// <summary>
    /// Compute the file extensions that can load the specified type
    /// </summary>
    /// <param name="type">The type to compute extensions for</param>
    /// <returns>A set of file extensions that can load the specified type</returns>
    private HashSet<string> ComputeExtensionsForType(Type type)
    {
        HashSet<string> extensions = new HashSet<string>();

        lock (_lockExtensions)
        {
            // Find all loaders that can handle this type
            foreach (var loader in _assetLoaders.Values)
            {
                if (loader.CanHandleType(type))
                {
                    foreach (var extension in loader.FileExtensions)
                    {
                        extensions.Add(extension);
                    }
                }
            }
        }

        return extensions;
    }

    private void UpdateEntries(bool forced = false)
    {
        if (!_isEntryDirty && !forced)
        {
            return;
        }

        lock (_lockEntry)
        {
            if (!_isEntryDirty && !forced)
            {
                return;
            }

            _fileEntries.Clear();
            _assetAliases.Clear();
            foreach (var fileSource in _fileSources)
            {
                foreach (var file in fileSource.AllFileNames)
                {
                    string key = ParseEntry(file);
                    _fileEntries[key] = fileSource;
                    string alias = Path.ChangeExtension(key, null);
                    _assetAliases[alias] = key;
                }
            }
            _isEntryDirty = false;
        }
    }

    private void UpdateRecongizedExtensions(bool forced = false)
    {
        if (!_isRecongizedExtensionsDirty && !forced)
        {
            return;
        }

        lock (_lockExtensions)
        {
            if (!_isRecongizedExtensionsDirty && !forced)
            {
                return;
            }
            
            _recongizedExtensions.Clear();
            foreach (var loader in _assetLoaders.Values)
            {
                foreach (var extension in loader.FileExtensions)
                {
                    _recongizedExtensions.Add(extension);
                }
            }
            _isRecongizedExtensionsDirty = false;
        }


    }

    private static string ParseEntry(string entry)
    {
        return entry.Replace('\\', '/');
    }
}