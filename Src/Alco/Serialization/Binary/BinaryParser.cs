using System;

namespace Alco;

/// <summary>Provides the typed binary serialization entry points over PackedBinary.</summary>
public class BinaryParser
{
    /// <summary>Encodes an object as a complete PackedBinary document without eliding defaults.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="obj">The object to encode.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional reference context.</param>
    /// <returns>The encoded document.</returns>
    public static ReadOnlyMemory<byte> Encode<T>(T obj, Action<string>? onError = null, ReferenceContext? referenceContext = null)
        where T : ISerializable =>
        PackedBinarySerializer.Encode(obj, refCtx: referenceContext, elideDefaults: false, onError: onError);

    /// <summary>Copies a document from a span, then loads a new object and resolves its references.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional reference context.</param>
    /// <returns>The loaded object.</returns>
    public static T Decode<T>(ReadOnlySpan<byte> bytes, Action<string>? onError = null, ReferenceContext? referenceContext = null)
        where T : ISerializable, new() => Decode<T>((ReadOnlyMemory<byte>)bytes.ToArray(), onError, referenceContext);

    /// <summary>Loads a new object and resolves its references.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes, immutable while any decoded views are in use.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional reference context.</param>
    /// <returns>The loaded object.</returns>
    public static T Decode<T>(ReadOnlyMemory<byte> bytes, Action<string>? onError = null, ReferenceContext? referenceContext = null)
        where T : ISerializable, new() => PackedBinarySerializer.Decode<T>(bytes, refCtx: referenceContext, onError: onError);

    /// <summary>Loads a new object from an array and resolves its references.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes, immutable while any decoded views are in use.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional reference context.</param>
    /// <returns>The loaded object.</returns>
    public static T Decode<T>(byte[] bytes, Action<string>? onError = null, ReferenceContext? referenceContext = null)
        where T : ISerializable, new() => Decode<T>((ReadOnlyMemory<byte>)bytes, onError, referenceContext);

    /// <summary>Copies a document from a span, then creates and loads an object using a factory.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="onCreate">The factory that can inspect the root before loading.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional reference context.</param>
    /// <returns>The loaded object with references resolved.</returns>
    public static T Decode<T>(ReadOnlySpan<byte> bytes, Func<SerializeReadNode, T> onCreate, Action<string>? onError = null,
        ReferenceContext? referenceContext = null) where T : ISerializable =>
        Decode((ReadOnlyMemory<byte>)bytes.ToArray(), onCreate, onError, referenceContext);

    /// <summary>Creates and loads an object using a factory.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes, immutable while any decoded views are in use.</param>
    /// <param name="onCreate">The factory that can inspect the root before loading.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional reference context.</param>
    /// <returns>The loaded object with references resolved.</returns>
    public static T Decode<T>(ReadOnlyMemory<byte> bytes, Func<SerializeReadNode, T> onCreate, Action<string>? onError = null,
        ReferenceContext? referenceContext = null) where T : ISerializable =>
        PackedBinarySerializer.Decode(bytes, onCreate, refCtx: referenceContext, onError: onError);

    /// <summary>Creates and loads an object from an array using a factory.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes, immutable while any decoded views are in use.</param>
    /// <param name="onCreate">The factory that can inspect the root before loading.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional reference context.</param>
    /// <returns>The loaded object with references resolved.</returns>
    public static T Decode<T>(byte[] bytes, Func<SerializeReadNode, T> onCreate, Action<string>? onError = null,
        ReferenceContext? referenceContext = null) where T : ISerializable =>
        Decode((ReadOnlyMemory<byte>)bytes, onCreate, onError, referenceContext);

    /// <summary>Copies the document bytes and loads an existing object without registering its root ID.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="obj">The existing target.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional context used to run post-load.</param>
    public static void Populate<T>(ReadOnlySpan<byte> bytes, T obj, Action<string>? onError = null, ReferenceContext? referenceContext = null)
        where T : ISerializable => Populate((ReadOnlyMemory<byte>)bytes.ToArray(), obj, onError, referenceContext);

    /// <summary>Loads an existing object without registering its root ID.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes, immutable while any decoded views are in use.</param>
    /// <param name="obj">The existing target.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional context used to run post-load.</param>
    public static void Populate<T>(ReadOnlyMemory<byte> bytes, T obj, Action<string>? onError = null, ReferenceContext? referenceContext = null)
        where T : ISerializable => PackedBinarySerializer.Populate(bytes, obj, refCtx: referenceContext, onError: onError);

    /// <summary>Loads an existing object from an array without registering its root ID.</summary>
    /// <typeparam name="T">The object type.</typeparam>
    /// <param name="bytes">The document bytes, immutable while any decoded views are in use.</param>
    /// <param name="obj">The existing target.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="referenceContext">The optional context used to run post-load.</param>
    public static void Populate<T>(byte[] bytes, T obj, Action<string>? onError = null, ReferenceContext? referenceContext = null)
        where T : ISerializable => Populate((ReadOnlyMemory<byte>)bytes, obj, onError, referenceContext);
}
