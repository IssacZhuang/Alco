using System;
using System.Buffers;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// Reusable PackedBinary encoder that retains internal scratch tables across sequential or reentrant encodes.
/// </summary>
public sealed class PackedBinaryEncoder
{
    private readonly int _initialStringCapacity;
    private readonly List<PackedBinaryWriteNode.WriteSession> _sessions = [];
    private int _activeDepth;

    /// <summary>
    /// Initializes a new reusable <see cref="PackedBinaryEncoder"/> instance.
    /// </summary>
    /// <param name="initialStringCapacity">Initial string table capacity allocated per scratch session.</param>
    public PackedBinaryEncoder(int initialStringCapacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialStringCapacity);
        _initialStringCapacity = initialStringCapacity;
    }

    /// <summary>
    /// Appends one complete PackedBinary document for <paramref name="root"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The buffer writer receiving the encoded document.</param>
    /// <param name="root">The object to encode.</param>
    /// <param name="refCtx">The optional reference ID context.</param>
    /// <param name="elideDefaults">Whether to omit scalar bindings equal to their defaults.</param>
    /// <param name="onError">The optional error callback.</param>
    public void EncodeTo(
        IBufferWriter<byte> destination,
        ISerializable root,
        ReferenceContext? refCtx = null,
        bool elideDefaults = false,
        Action<string>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(root);

        if (_activeDepth == _sessions.Count)
            _sessions.Add(new PackedBinaryWriteNode.WriteSession(new PackedErrors(onError), _initialStringCapacity, owner: this));

        PackedBinaryWriteNode.WriteSession session = _sessions[_activeDepth++];
        try
        {
            session.Reset(refCtx, elideDefaults, new PackedErrors(onError));
            PackedBinaryWriteNode.EncodeTo(session, destination, root);
        }
        finally
        {
            session.Reset(null, elideDefaults, new PackedErrors(null));
            _activeDepth--;
        }
    }
}
