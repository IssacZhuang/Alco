namespace Alco.IO;

/// <summary>
/// Base encoder for a single asset type.
/// </summary>
/// <typeparam name="T">The asset type this encoder supports.</typeparam>
public abstract class BaseAssetEncoder<T> : IAssetEncoder
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
    /// Encodes an asset into its serialized byte form.
    /// </summary>
    /// <param name="asset">The asset to encode.</param>
    /// <returns>The encoded data buffer.</returns>
    public abstract SafeMemoryHandle Encode(object asset);
}

