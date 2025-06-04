using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

public sealed partial class AssetSystem
{
    private static readonly ConcurrentDictionary<Type, IAssetEncoder> _encoders = new();

    /// <summary>
    /// Registers an asset encoder that can write assets of a specific type.
    /// </summary>
    /// <param name="writer">The asset encoder to register.</param>
    public void RegisterAssetEncoder(IAssetEncoder writer)
    {
        foreach (var type in writer.GetSupportedTypes())
        {
            _encoders[type] = writer;
        }
    }

    /// <summary>
    /// Unregisters an asset encoder.
    /// </summary>
    /// <param name="writer">The asset encoder to unregister.</param>
    public void UnregisterAssetEncoder(IAssetEncoder writer)
    {
        foreach (var type in writer.GetSupportedTypes())
        {
            if (_encoders.TryGetValue(type, out var value))
            {
                if (ReferenceEquals(value, writer))
                {
                    _encoders.TryRemove(type, out _);
                }
            }
        }
    }

    /// <summary>
    /// Encodes an asset to binary format using the registered encoder for its type.
    /// </summary>
    /// <typeparam name="T">The type of the asset to encode.</typeparam>
    /// <param name="asset">The asset to encode.</param>
    /// <returns>A handle to the encoded binary data.</returns>
    /// <exception cref="ArgumentNullException">Thrown when asset is null.</exception>
    public SafeMemoryHandle EncodeToBinary<T>(T asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return EncodeToBinary(asset, typeof(T));
    }

    /// <summary>
    /// Encodes an asset to binary format using the registered encoder for the specified type.
    /// </summary>
    /// <param name="asset">The asset to encode.</param>
    /// <param name="type">The type of the asset.</param>
    /// <returns>A handle to the encoded binary data.</returns>
    /// <exception cref="Exception">Thrown when no encoder is registered for the specified type.</exception>
    public SafeMemoryHandle EncodeToBinary(object asset, Type type)
    {
        if (_encoders.TryGetValue(type, out IAssetEncoder? writer))
        {
            return writer.Encode(asset);
        }
        throw new Exception($"No writer found for type {type.Name}");
    }
}