using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Alco;

/// <summary>
/// Encodes and decodes object graphs through PackedBinary v1.
/// </summary>
public static class PackedBinarySerializer
{
    /// <summary>
    /// Encodes a root object as a complete PackedBinary document.
    /// </summary>
    /// <param name="root">The object to encode.</param>
    /// <param name="refCtx">The optional reference ID context.</param>
    /// <param name="elideDefaults">Whether to omit scalar bindings equal to their defaults.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <returns>The encoded document.</returns>
    public static byte[] Encode(ISerializable root, ReferenceContext? refCtx = null, bool elideDefaults = true, Action<string>? onError = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        EncodeTo(buffer, root, refCtx, elideDefaults, onError);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>
    /// Appends one complete PackedBinary document to a buffer writer.
    /// </summary>
    /// <param name="destination">The writer to append to.</param>
    /// <param name="root">The object to encode.</param>
    /// <param name="refCtx">The optional reference ID context.</param>
    /// <param name="elideDefaults">Whether to omit scalar bindings equal to their defaults.</param>
    /// <param name="onError">The optional error callback.</param>
    public static void EncodeTo(IBufferWriter<byte> destination, ISerializable root, ReferenceContext? refCtx = null,
        bool elideDefaults = true, Action<string>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(root);
        PackedBinaryWriteNode.Encoder.EncodeTo(destination, root, refCtx, elideDefaults, new PackedErrors(onError));
    }

    /// <summary>
    /// Loads a new object, then resolves its references.
    /// </summary>
    /// <typeparam name="T">The type to create.</typeparam>
    /// <param name="data">The document, which must remain immutable while any decoded binary views are in use.</param>
    /// <param name="factory">The factory that can inspect the root table before loading.</param>
    /// <param name="refCtx">The optional reference ID context.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <returns>The loaded object.</returns>
    /// <exception cref="InvalidDataException">The root document is invalid.</exception>
    public static T Decode<T>(ReadOnlyMemory<byte> data, Func<SerializeReadNode, T> factory, ReferenceContext? refCtx = null,
        Action<string>? onError = null) where T : ISerializable
    {
        if (TryDecode(data, factory, out T? value, refCtx, onError))
            return value;

        throw new InvalidDataException("Invalid PackedBinary document.");
    }

    /// <summary>
    /// Loads a new default-constructed object, then resolves its references.
    /// </summary>
    /// <typeparam name="T">The type to create.</typeparam>
    /// <param name="data">The document, which must remain immutable while any decoded binary views are in use.</param>
    /// <param name="refCtx">The optional reference ID context.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <returns>The loaded object.</returns>
    public static T Decode<T>(ReadOnlyMemory<byte> data, ReferenceContext? refCtx = null, Action<string>? onError = null)
        where T : ISerializable, new() => Decode(data, static _ => new T(), refCtx, onError);

    /// <summary>
    /// Validates the root document before creating and loading a new object.
    /// </summary>
    /// <typeparam name="T">The type to create.</typeparam>
    /// <param name="data">The document, which must remain immutable while any decoded binary views are in use.</param>
    /// <param name="factory">The factory that can inspect the root table before loading.</param>
    /// <param name="value">The loaded object on success.</param>
    /// <param name="refCtx">The optional reference ID context.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <returns>Whether the root document was valid. User code and callback exceptions still propagate.</returns>
    public static bool TryDecode<T>(ReadOnlyMemory<byte> data, Func<SerializeReadNode, T> factory, [NotNullWhen(true)] out T? value,
        ReferenceContext? refCtx = null, Action<string>? onError = null) where T : ISerializable
    {
        ArgumentNullException.ThrowIfNull(factory);
        value = default;
        if (!PackedBinaryReadNode.TryOpen(data, out PackedBinaryReadNode? node, refCtx, onError))
            return false;

        value = factory(node);
        refCtx?.TryReadReferenceId(node, value);
        value.OnSerialize(node, SerializeMode.Load);
        value.OnSerialize(node.CreatePostLoadNode(), SerializeMode.PostLoad);
        return true;
    }

    /// <summary>
    /// Loads into an existing object without registering its root ID. Runs post-load when a context is supplied.
    /// </summary>
    /// <typeparam name="T">The type to populate.</typeparam>
    /// <param name="data">The document, which must remain immutable while any decoded binary views are in use.</param>
    /// <param name="target">The existing object to load into.</param>
    /// <param name="refCtx">The optional reference ID context.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <param name="clearMissingReferences">Whether post-load clears stale references on reused objects.</param>
    /// <exception cref="InvalidDataException">The root document is invalid.</exception>
    public static void Populate<T>(ReadOnlyMemory<byte> data, T target, ReferenceContext? refCtx = null,
        Action<string>? onError = null, bool clearMissingReferences = false) where T : ISerializable
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (!PackedBinaryReadNode.TryOpen(data, out PackedBinaryReadNode? node, refCtx, onError))
            throw new InvalidDataException("Invalid PackedBinary document.");

        target.OnSerialize(node, SerializeMode.Load);
        if (refCtx is not null)
            target.OnSerialize(node.CreatePostLoadNode(clearMissingReferences), SerializeMode.PostLoad);
    }
}

internal sealed class PackedErrors(Action<string>? onError)
{
    private Exception? _callbackFailure;

    public bool CanReport(Exception error) => error is not (OutOfMemoryException or OperationCanceledException) &&
        !ReferenceEquals(error, _callbackFailure);

    public void Report(string message)
    {
        try
        {
            if (onError is null)
                Log.Error(message);
            else
                onError(message);
        }
        catch (Exception error)
        {
            _callbackFailure = error;
            throw;
        }
    }
}
