namespace Alco.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// Get all the file names of the assets
    /// </summary>
    public IEnumerable<string> AllFileNames
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
    public IEnumerable<string> AllFileAliases
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
            return AllFileNames.Select(x => new AssetInfo(this, x));
        }
    }

    /// <summary>
    /// Check if the file exists
    /// /// </summary>
    /// <param name="filename">The filename to check</param>
    /// <returns>True if the file exists</returns>
    public bool IsFileExist(string filename)
    {
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
        _fileSources.Add(fileSource);
        _isEntryDirty = true;
    }

    /// <summary>
    /// Remove the file source from the asset manager
    /// </summary>
    /// <param name="fileSource">The file source to remove</param>
    public void RemoveFileSource(IFileSource fileSource)
    {
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