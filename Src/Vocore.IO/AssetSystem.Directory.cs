

namespace Vocore.IO;

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
        if (_fileSources.Remove(fileSource))
        {
            fileSource.OnUnload();
        }
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

        _fileEntries.Clear();
        foreach (var fileSource in _fileSources)
        {
            foreach (var file in fileSource.AllFileNames)
            {
                string extension = Path.GetExtension(file);


                if (_recongizedExtensions.Contains(extension))
                {
                    //Log.Info($"Add file entry: {file}");
                    if (!_fileEntries.TryAdd(ParseEntry(file), fileSource))
                    {
                        Log.Warning($"File entry already added: {file}");
                    }
                }
            }
        }
        _isEntryDirty = false;
    }

    private static string ParseEntry(string entry)
    {
        return entry.Replace('\\', '/');
    }

    private void UpdateRecongizedExtensions(bool forced = false)
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