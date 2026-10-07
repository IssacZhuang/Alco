using Alco.Editor.Extensibility;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// No-op hot reloader for meta sidecars. Saving a <c>.meta</c> from the editor fires
/// the directory watcher, and <see cref="AssetSystem"/> throws when a cached asset
/// type has no reloader (async-void path — it would escape onto the main thread).
/// Registering this swallow keeps meta saves harmless; the editing document evicts and
/// rebuilds its own state explicitly. The supported <see cref="Meta"/> subclasses come
/// from the <see cref="MetaTypeRegistry"/> filled by the editor modules (the built-in
/// module registers <see cref="Texture2DMeta"/>).
/// </summary>
public sealed class MetaHotReloader : IAssetHotReloader
{
    private readonly MetaTypeRegistry _metaTypes;

    /// <summary>Creates the reloader over the meta types in <paramref name="metaTypes"/>.</summary>
    public MetaHotReloader(MetaTypeRegistry metaTypes)
    {
        ArgumentNullException.ThrowIfNull(metaTypes);
        _metaTypes = metaTypes;
    }

    /// <inheritdoc/>
    public IEnumerable<Type> GetSupportedTypes() => _metaTypes.Types;

    /// <inheritdoc/>
    public void HotReload(object asset, ReadOnlySpan<byte> data)
    {
        // Meta sidecars carry no live GPU state; consumers re-read them on next load.
    }
}
