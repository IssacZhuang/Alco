
namespace Alco.IO;

/// <summary>
/// Base hot reloader for a single asset type.
/// </summary>
/// <typeparam name="T">The asset type this hot reloader supports.</typeparam>
public abstract class BaseAssetHotReloader<T> : IAssetHotReloader
{
    /// <summary>
    /// Returns the supported asset types.
    /// </summary>
    /// <returns>A sequence containing <typeparamref name="T"/>.</returns>
    public IEnumerable<Type> GetSupportedTypes()
    {
        yield return typeof(T);
    }

    /// <summary>
    /// Reloads an existing asset from new serialized data.
    /// </summary>
    /// <param name="asset">The asset to reload.</param>
    /// <param name="data">The new serialized data.</param>
    public abstract void HotReload(object asset, ReadOnlySpan<byte> data);
}

