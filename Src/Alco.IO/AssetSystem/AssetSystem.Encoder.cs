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

    /// <summary>
    /// Attempts to write an asset to the specified path.
    /// </summary>
    /// <param name="path">The path where the asset should be written.</param>
    /// <param name="asset">The asset to write.</param>
    /// <param name="type">The type of the asset.</param>
    /// <param name="failureReason">When the method returns false, contains the reason for the failure.</param>
    /// <returns>True if the asset was written successfully; otherwise, false.</returns>
    public bool TryWriteAsset(string path, object asset, Type type, [NotNullWhen(false)] out string? failureReason)
    {
        TryRefreshEntries();
        if (!_fileEntries.TryGetValue(path, out IFileSource? fileSource))
        {
            failureReason = $"Asset not found: {path}";
            return false;
        }

        if (!fileSource.IsWriteable)
        {
            failureReason = $"Asset is read-only: {path}";
            return false;
        }

        try
        {
            using var handle = EncodeToBinary(asset, type);
            if (!fileSource.TryWriteData(path, handle.AsReadOnlySpan(), out failureReason))
            {
                return false;
            }
        }
        catch (Exception e)
        {
            failureReason = e.ToString();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Attempts to write an asset to the specified path.
    /// </summary>
    /// <param name="path">The path where the asset should be written.</param>
    /// <param name="asset">The asset to write.</param>
    /// <param name="type">The type of the asset.</param>
    /// <returns>True if the asset was written successfully; otherwise, false.</returns>
    public bool TryWriteAsset(string path, object asset, Type type)
    {
        return TryWriteAsset(path, asset, type, out _);
    }

    /// <summary>
    /// Attempts to write an asset to the specified path.
    /// </summary>
    /// <typeparam name="T">The type of the asset.</typeparam>
    /// <param name="path">The path where the asset should be written.</param>
    /// <param name="asset">The asset to write.</param>
    /// <param name="failureReason">When the method returns false, contains the reason for the failure.</param>
    /// <returns>True if the asset was written successfully; otherwise, false.</returns>
    public bool TryWriteAsset<T>(string path, T asset, [NotNullWhen(false)] out string? failureReason) where T : notnull
    {
        return TryWriteAsset(path, asset, typeof(T), out failureReason);
    }

    /// <summary>
    /// Attempts to write an asset to the specified path.
    /// </summary>
    /// <typeparam name="T">The type of the asset.</typeparam>
    /// <param name="path">The path where the asset should be written.</param>
    /// <param name="asset">The asset to write.</param>
    /// <returns>True if the asset was written successfully; otherwise, false.</returns>
    public bool TryWriteAsset<T>(string path, T asset) where T : notnull
    {
        return TryWriteAsset(path, asset, typeof(T), out _);
    }

    /// <summary>
    /// Writes an asset to the specified path. Throws an exception if the write operation fails.
    /// </summary>
    /// <typeparam name="T">The type of the asset.</typeparam>
    /// <param name="path">The path where the asset should be written.</param>
    /// <param name="asset">The asset to write.</param>
    /// <exception cref="Exception">Thrown when the write operation fails.</exception>
    public void WriteAsset<T>(string path, T asset) where T : notnull
    {
        if (!TryWriteAsset(path, asset, typeof(T), out _))
        {
            throw new Exception($"Failed to write asset: {path}");
        }
    }

    /// <summary>
    /// Writes an asset to the specified path. Throws an exception if the write operation fails.
    /// </summary>
    /// <param name="path">The path where the asset should be written.</param>
    /// <param name="asset">The asset to write.</param>
    /// <param name="type">The type of the asset.</param>
    /// <exception cref="Exception">Thrown when the write operation fails.</exception>
    public void WriteAsset(string path, object asset, Type type)
    {
        if (!TryWriteAsset(path, asset, type, out _))
        {
            throw new Exception($"Failed to write asset: {path}");
        }
    }
}