using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Alco.IO;

/// <summary>
/// Reusable builder that assembles <c>ACHK</c> TLV section payloads with zero redundant intermediate copies.
/// </summary>
public sealed class ChunkPayloadBuilder
{
    private readonly List<SectionEntry> _sections = [];
    private readonly SectionBufferWriter _writer = new();
    private double _lastSimTime;
    private bool _sectionOpen;
    private int _openSectionStart;

    /// <summary>
    /// Gets or sets the absolute simulation timestamp (in seconds) stored in the chunk header.
    /// </summary>
    public double LastSimTime
    {
        get => _lastSimTime;
        set => _lastSimTime = value;
    }

    /// <summary>
    /// Gets the number of committed sections in the current payload.
    /// </summary>
    public int SectionCount => _sections.Count;

    /// <summary>
    /// Clears all committed sections and resets the builder for reuse.
    /// </summary>
    /// <param name="lastSimTime">The simulation timestamp for the next payload.</param>
    public void Reset(double lastSimTime = 0.0)
    {
        _lastSimTime = lastSimTime;
        _sections.Clear();
        _writer.Rewind(0);
        _sectionOpen = false;
        _openSectionStart = 0;
    }

    /// <summary>
    /// Appends a complete section to the payload.
    /// </summary>
    /// <param name="sectionId">The unique section identifier.</param>
    /// <param name="data">The section bytes.</param>
    /// <param name="flags">Optional section flags.</param>
    public void AddSection(ushort sectionId, ReadOnlySpan<byte> data, ushort flags = 0)
    {
        if (_sectionOpen)
            throw new InvalidOperationException("Cannot add a section while another section write is open.");

        EnsureUniqueSectionId(sectionId);
        int offset = _writer.WrittenCount;
        if (!data.IsEmpty)
        {
            data.CopyTo(_writer.GetSpan(data.Length));
            _writer.Advance(data.Length);
        }

        _sections.Add(new SectionEntry(sectionId, flags, offset, data.Length));
    }

    /// <summary>
    /// Begins writing a section directly into the builder's internal buffer.
    /// </summary>
    /// <returns>A buffer writer that receives the section payload until <see cref="CommitSection"/> or <see cref="AbortSection"/> is called.</returns>
    public IBufferWriter<byte> BeginSection()
    {
        if (_sectionOpen)
            throw new InvalidOperationException("A section write is already open.");

        _sectionOpen = true;
        _openSectionStart = _writer.WrittenCount;
        return _writer;
    }

    /// <summary>
    /// Commits the currently open section write with the given identifier and flags.
    /// </summary>
    /// <param name="sectionId">The unique section identifier.</param>
    /// <param name="flags">Optional section flags.</param>
    public void CommitSection(ushort sectionId, ushort flags = 0)
    {
        if (!_sectionOpen)
            throw new InvalidOperationException("No section write is currently open.");

        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].SectionId == sectionId)
            {
                _writer.Rewind(_openSectionStart);
                _sectionOpen = false;
                throw new ArgumentException($"Duplicate section ID {sectionId}.", nameof(sectionId));
            }
        }

        int length = _writer.WrittenCount - _openSectionStart;
        _sections.Add(new SectionEntry(sectionId, flags, _openSectionStart, length));
        _sectionOpen = false;
    }

    /// <summary>
    /// Aborts the currently open section write and discards all bytes written since <see cref="BeginSection"/>.
    /// </summary>
    public void AbortSection()
    {
        if (!_sectionOpen)
            throw new InvalidOperationException("No section write is currently open.");

        _writer.Rewind(_openSectionStart);
        _sectionOpen = false;
    }

    /// <summary>
    /// Writes the complete <c>ACHK</c> header, section table, and contiguous section payloads to <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The target buffer writer.</param>
    public void WriteTo(IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (_sectionOpen)
            throw new InvalidOperationException("Cannot serialize payload while a section write is still open.");
        if (_sections.Count > ushort.MaxValue)
            throw new InvalidOperationException("Section count exceeds ushort.MaxValue.");

        int headerAndTableSize = StreamingSaveFormat.ChunkPayloadHeaderSize + _sections.Count * StreamingSaveFormat.ChunkSectionDescSize;
        Span<byte> header = destination.GetSpan(headerAndTableSize)[..headerAndTableSize];

        StreamingSaveFormat.ChunkPayloadMagicBytes.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), StreamingSaveFormat.ChunkPayloadVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), (ushort)_sections.Count);
        BinaryPrimitives.WriteDoubleLittleEndian(header.Slice(8, 8), _lastSimTime);

        for (int i = 0; i < _sections.Count; i++)
        {
            SectionEntry entry = _sections[i];
            int descOffset = StreamingSaveFormat.ChunkPayloadHeaderSize + i * StreamingSaveFormat.ChunkSectionDescSize;
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(descOffset, 2), entry.SectionId);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(descOffset + 2, 2), entry.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(descOffset + 4, 4), checked((uint)entry.Length));
        }

        destination.Advance(headerAndTableSize);

        ReadOnlySpan<byte> sectionData = _writer.WrittenSpan;
        if (!sectionData.IsEmpty)
        {
            sectionData.CopyTo(destination.GetSpan(sectionData.Length));
            destination.Advance(sectionData.Length);
        }
    }

    /// <summary>
    /// Serializes the complete <c>ACHK</c> payload into a newly allocated byte array.
    /// </summary>
    /// <returns>The encoded chunk payload bytes.</returns>
    public byte[] ToArray()
    {
        if (_sectionOpen)
            throw new InvalidOperationException("Cannot serialize payload while a section write is still open.");

        int totalSize = StreamingSaveFormat.ChunkPayloadHeaderSize +
                        _sections.Count * StreamingSaveFormat.ChunkSectionDescSize +
                        _writer.WrittenCount;
        var writer = new ArrayBufferWriter<byte>(totalSize);
        WriteTo(writer);
        return writer.WrittenSpan.ToArray();
    }

    private void EnsureUniqueSectionId(ushort sectionId)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].SectionId == sectionId)
                throw new ArgumentException($"Duplicate section ID {sectionId}.", nameof(sectionId));
        }
    }

    private readonly record struct SectionEntry(ushort SectionId, ushort Flags, int Offset, int Length);

    private sealed class SectionBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[4096];
        private int _written;

        public int WrittenCount => _written;

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        public void Rewind(int position) => _written = position;

        public void Advance(int count)
        {
            if (count < 0 || _written + count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        private void EnsureCapacity(int sizeHint)
        {
            int required = Math.Max(1, sizeHint);
            if (_written + required <= _buffer.Length)
                return;

            int newSize = _buffer.Length;
            int target = checked(_written + required);
            while (newSize < target)
            {
                newSize = newSize <= Array.MaxLength / 2 ? newSize * 2 : Array.MaxLength;
            }

            Array.Resize(ref _buffer, newSize);
        }
    }
}
