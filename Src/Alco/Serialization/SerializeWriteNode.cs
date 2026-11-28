using System;
using System.Buffers;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// Base class for nodes that write values to serialized data.
/// </summary>
public abstract class SerializeWriteNode : SerializeNode
{
    /// <summary>
    /// Scoped temporary buffer writer borrowed from the active encoder session.
    /// Must be disposed in strict LIFO order.
    /// </summary>
    public readonly struct ScratchBufferScope : IDisposable
    {
        private readonly ArrayBufferWriter<byte> _writer;
        private readonly IDisposable? _releaser;

        /// <summary>Initializes a scratch buffer scope wrapping <paramref name="writer"/>.</summary>
        /// <param name="writer">The underlying array buffer writer.</param>
        /// <param name="releaser">Optional disposable invoked when the scope ends.</param>
        public ScratchBufferScope(ArrayBufferWriter<byte> writer, IDisposable? releaser = null)
        {
            ArgumentNullException.ThrowIfNull(writer);
            _writer = writer;
            _releaser = releaser;
        }

        /// <summary>Gets the scratch buffer writer.</summary>
        public ArrayBufferWriter<byte> Writer => _writer;

        /// <summary>Gets the bytes written to the scratch buffer.</summary>
        public ReadOnlySpan<byte> WrittenSpan => _writer.WrittenSpan;

        /// <summary>Gets the memory view of bytes written to the scratch buffer.</summary>
        public ReadOnlyMemory<byte> WrittenMemory => _writer.WrittenMemory;

        /// <summary>Gets the number of bytes written to the scratch buffer.</summary>
        public int WrittenCount => _writer.WrittenCount;

        /// <inheritdoc />
        public void Dispose() => _releaser?.Dispose();
    }

    /// <summary>
    /// Rents a temporary scratch buffer reused from the active write session when supported.
    /// Callers must dispose returned scopes in strict LIFO order.
    /// </summary>
    public virtual ScratchBufferScope RentScratchBuffer() => new(new ArrayBufferWriter<byte>(), releaser: null);

    /// <summary>
    /// Writes raw binary bytes under <paramref name="key"/> without requiring an owned <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    /// <param name="key">The key identifying the binary field.</param>
    /// <param name="data">The bytes to write.</param>
    public virtual void SetBinary(string key, ReadOnlySpan<byte> data)
    {
        ReadOnlyMemory<byte> copy = data.ToArray();
        BindBinary(key, ref copy);
    }

    /// <summary>
    /// Attempts to reserve a contiguous <see cref="PackedTag.Binary"/> payload slot directly in the backing write buffer.
    /// </summary>
    /// <param name="key">The key identifying the binary field.</param>
    /// <param name="byteLength">The exact payload length in bytes to reserve and write into <paramref name="destination"/>.</param>
    /// <param name="destination">When <see langword="true"/>, receives the writable span of length <paramref name="byteLength"/>.</param>
    /// <returns><see langword="true"/> if direct reservation is supported; otherwise <see langword="false"/>.</returns>
    public virtual bool TryReserveBinary(string key, int byteLength, out Span<byte> destination)
    {
        destination = default;
        return false;
    }

    /// <summary>
    /// Encodes a nested standalone document into <paramref name="destination"/>, reusing encoder scratch when supported.
    /// </summary>
    /// <param name="destination">The buffer writer receiving the encoded document.</param>
    /// <param name="root">The serializable object to encode.</param>
    /// <param name="refCtx">Optional reference context shared with the nested document.</param>
    /// <param name="elideDefaults">Whether default-valued fields may be omitted.</param>
    public virtual void EncodeSubDocument(
        IBufferWriter<byte> destination,
        ISerializable root,
        ReferenceContext? refCtx = null,
        bool elideDefaults = false) =>
        PackedBinarySerializer.EncodeTo(destination, root, refCtx, elideDefaults);

    public override void BindEnum<T>(string key, ref T value, T @default = default)
    {
        SetEnum(key, value);
    }

    public override void BindString(string key, ref string value, string @default = "")
    {
        SetString(key, value);
    }

    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        SetValue(key, value);
    }

    /// <summary>
    /// Binds a nullable unmanaged value by writing it as a single entry.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to bind.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <param name="value">Reference to the nullable value to be serialized.</param>
    public override void BindValue<T>(string key, ref T? value)
    {
        SetNullableValue(key, value);
    }

    /// <summary>
    /// Writes an unmanaged value under the given key.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type.</typeparam>
    /// <param name="key">The key identifying the value.</param>
    /// <param name="value">The value to write.</param>
    public abstract void SetValue<T>(string key, T value) where T : unmanaged;

    /// <summary>
    /// Writes a nullable unmanaged value under the given key.
    /// </summary>
    /// <typeparam name="T">The unmanaged value type to write.</typeparam>
    /// <param name="key">The key identifier for the value in the serialization format.</param>
    /// <param name="value">The nullable value to write.</param>
    public abstract void SetNullableValue<T>(string key, T? value) where T : unmanaged;
    /// <summary>
    /// Writes an enum value under the given key.
    /// </summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="key">The key identifying the value.</param>
    /// <param name="value">The value to write.</param>
    public abstract void SetEnum<T>(string key, T value) where T : unmanaged, Enum;
    /// <summary>
    /// Writes a string under the given key.
    /// </summary>
    /// <param name="key">The key identifying the string.</param>
    /// <param name="value">The string to write.</param>
    public abstract void SetString(string key, string value);
}