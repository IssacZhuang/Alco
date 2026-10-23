namespace Alco.IO;

/// <summary>
/// Applies reloaded serialized data to existing asset instances of supported types.
/// </summary>
public interface IAssetHotReloader
{
    /// <summary>
    /// Gets the asset types this hot reloader supports.
    /// </summary>
    IEnumerable<Type> GetSupportedTypes();
    /// <summary>
    /// Reloads an existing asset from new serialized data.
    /// </summary>
    /// <param name="asset">The asset instance to reload.</param>
    /// <param name="data">The new serialized data.</param>
    void HotReload(object asset, ReadOnlySpan<byte> data);
}