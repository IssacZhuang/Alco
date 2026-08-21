namespace Alco.IO;

public readonly ref struct AssetLoadContext
{
    public readonly AssetSystem AssetSystem;
    public readonly string Filename;
    public readonly Type AssetType;

    private readonly DataLoader? _loader;
    private readonly ReadOnlySpan<byte> _preloadedData;

    /// <summary>
    /// Creates a context with lazy data loading. Data is loaded on first <see cref="GetData"/> call.
    /// </summary>
    public AssetLoadContext(AssetSystem assetSystem, string filename, Type assetType)
    {
        AssetSystem = assetSystem;
        Filename = filename;
        AssetType = assetType;
        _preloadedData = default;
        _loader = new DataLoader(assetSystem, filename);
    }

    /// <summary>
    /// Creates a context with pre-loaded data. <see cref="GetData"/> returns the data directly without I/O.
    /// </summary>
    public AssetLoadContext(AssetSystem assetSystem, string filename, ReadOnlySpan<byte> data, Type assetType)
    {
        AssetSystem = assetSystem;
        Filename = filename;
        AssetType = assetType;
        _preloadedData = data;
        _loader = null;
    }

    /// <summary>
    /// Returns the raw asset data. On the lazy path, triggers file I/O on first call and caches the result.
    /// </summary>
    /// <exception cref="InvalidOperationException">Data could not be loaded from the file source.</exception>
    public ReadOnlySpan<byte> GetData()
    {
        return _loader != null
            ? _loader.Load()
            : _preloadedData;
    }

    /// <summary>
    /// Gets a value indicating whether a seekable stream can be opened for this asset
    /// (true for file-backed contexts, false for preloaded in-memory contexts).
    /// </summary>
    public bool CanGetStream => _loader != null;

    /// <summary>
    /// Opens a seekable read stream over the asset file without loading it into memory.
    /// Ownership of the stream transfers to the caller; dispose it when done. Loaders that
    /// keep streaming after CreateAsset (e.g. cooked meshes) may hand the stream to the
    /// created asset instead of disposing it here.
    /// </summary>
    /// <returns>The seekable asset stream.</returns>
    /// <exception cref="InvalidOperationException">Thrown for preloaded contexts or when the
    /// file source cannot open a stream.</exception>
    public Stream GetStream()
    {
        if (_loader == null)
        {
            throw new InvalidOperationException($"No stream available for preloaded asset '{Filename}'.");
        }

        return _loader.GetStream();
    }

    /// <summary>
    /// Disposes any data loaded by <see cref="GetData"/>. No-op for pre-loaded contexts.
    /// </summary>
    internal void DisposeLoadedData() => _loader?.Dispose();

    private sealed class DataLoader
    {
        private readonly AssetSystem _assetSystem;
        private readonly string _filename;
        private SafeMemoryHandle? _handle;
        private bool _loaded;

        public DataLoader(AssetSystem assetSystem, string filename)
        {
            _assetSystem = assetSystem;
            _filename = filename;
        }

        public ReadOnlySpan<byte> Load()
        {
            if (!_loaded)
            {
                if (!_assetSystem.TryLoadDataFromSource(_filename, out _handle))
                    throw new InvalidOperationException($"Failed to load data for asset '{_filename}'");
                _loaded = true;
            }
            return _handle != null ? _handle.AsReadOnlySpan() : ReadOnlySpan<byte>.Empty;
        }

        public Stream GetStream()
        {
            if (!_assetSystem.TryGetStreamFromSource(_filename, out Stream? stream) || stream == null)
            {
                throw new InvalidOperationException($"Failed to open stream for asset '{_filename}'");
            }

            return stream;
        }

        public void Dispose()
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
