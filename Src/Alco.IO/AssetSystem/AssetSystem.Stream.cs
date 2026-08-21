using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    /// <summary>
    /// <c>[Thread Safe]</c> Try to open a seekable read stream over an asset file without
    /// loading it into memory. Intended for streaming assets (cooked meshes, future texture
    /// streaming) that need positional access after the initial load. The caller owns and must
    /// dispose the returned stream.
    /// </summary>
    /// <param name="filename">The filename or alias of the asset.</param>
    /// <param name="stream">The seekable asset stream when successful.</param>
    /// <returns>True if the stream was opened; otherwise false.</returns>
    public bool TryGetStream(string filename, [NotNullWhen(true)] out Stream? stream)
    {
        TryRefreshEntries();

        filename = ParseEntry(filename);

        if (!IsFileExist(filename, out string realFilename))
        {
            stream = null;
            return false;
        }

        return TryGetStreamFromSource(realFilename, out stream);
    }

    /// <summary>
    /// Try to open a seekable read stream for a resolved filename directly from its owning
    /// file source.
    /// </summary>
    /// <param name="filename">The resolved filename.</param>
    /// <param name="stream">The seekable asset stream when successful.</param>
    /// <returns>True if the stream was opened; otherwise false.</returns>
    internal bool TryGetStreamFromSource(string filename, [NotNullWhen(true)] out Stream? stream)
    {
        if (_fileEntries.TryGetValue(filename, out IFileSource? fileSource))
        {
            return fileSource.TryGetStream(filename, out stream, out _);
        }

        stream = null;
        return false;
    }

    /// <summary>
    /// <c>[Thread Safe]</c> Remove an asset from the cache. When the cached asset is
    /// <see cref="IDisposable"/> it is disposed — this is the deterministic eviction path for
    /// streaming assets that hold file handles or GPU resources. Assets still referenced by
    /// the caller must not be used afterwards.
    /// </summary>
    /// <param name="filename">The filename or alias of the asset.</param>
    /// <returns>True when a cache entry was found and removed; false when nothing was cached.</returns>
    public bool Unload(string filename)
    {
        filename = ParseEntry(filename);

        if (IsFileExist(filename, out string realFilename))
        {
            filename = realFilename;
        }

        if (_assetLookup.TryRemove(filename, out AssetHandle? handle))
        {
            if (handle.CachedAsset is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return true;
        }

        return false;
    }
}
