using Alco.IO;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// No-op hot reloader for meta sidecars. Saving a <c>.meta</c> from the editor fires
/// the directory watcher, and <see cref="AssetSystem"/> throws when a cached asset
/// type has no reloader (async-void path — it would escape onto the main thread).
/// Registering this swallow keeps meta saves harmless; the editing document evicts and
/// rebuilds its own state explicitly. New <see cref="Meta"/> subclasses must be added
/// to <see cref="GetSupportedTypes"/> when they appear.
/// </summary>
public sealed class MetaHotReloader : IAssetHotReloader
{
    /// <inheritdoc/>
    public IEnumerable<Type> GetSupportedTypes()
    {
        yield return typeof(Texture2DMeta);
    }

    /// <inheritdoc/>
    public void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        // Meta sidecars carry no live GPU state; consumers re-read them on next load.
    }
}
