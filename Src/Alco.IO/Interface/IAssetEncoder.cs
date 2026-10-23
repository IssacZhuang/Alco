namespace Alco.IO;

/// <summary>
/// Encodes supported asset types into serialized buffers.
/// </summary>
public interface IAssetEncoder
{
    /// <summary>
    /// Gets the asset types this encoder supports.
    /// </summary>
    IEnumerable<Type> GetSupportedTypes();
    /// <summary>
    /// Encodes an asset into a serialized memory buffer.
    /// </summary>
    /// <param name="asset">The asset instance to encode.</param>
    /// <returns>The serialized asset data.</returns>
    SafeMemoryHandle Encode(object asset);
}

