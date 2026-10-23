using Alco.Editor.Extensibility;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Editor;

/// <summary>
/// No-op hot reloader for meta sidecars: registering it keeps editor saves of <c>.meta</c> files harmless, since <see cref="AssetSystem"/> requires a reloader for cached types while the editing document rebuilds its own state explicitly.
/// The supported types come from the <see cref="MetaTypeRegistry"/> filled by the editor modules.
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
