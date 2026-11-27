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
        if (_sectionOpen)
            throw new InvalidOperationException("Cannot reset builder while a section scope is active.");

        _lastSimTime = lastSimTime;
        _sections.Clear();
        _writer.Rewind(0);
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
    /// Disposing the returned scope without calling <see cref="SectionScope.Commit"/> rewinds any partial writes.
    /// </summary>
    /// <param name="sectionId">The unique section identifier, validated eagerly.</param>
    /// <param name="flags">Optional section flags.</param>
    /// <returns>A disposable write scope bound to this builder.</returns>
    public SectionScope BeginSection(ushort sectionId, ushort flags = 0)
    {
        if (_sectionOpen)
            throw new InvalidOperationException("A section write is already open.");

        EnsureUniqueSectionId(sectionId);
        _sectionOpen = true;
        _openSectionStart = _writer.WrittenCount;
        return new SectionScope(this, sectionId, flags);
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

        int headerAndTableSize = ChunkRecordCodec.ChunkPayloadHeaderSize + _sections.Count * ChunkRecordCodec.ChunkSectionDescSize;
        Span<byte> header = destination.GetSpan(headerAndTableSize)[..headerAndTableSize];

        ChunkRecordCodec.ChunkPayloadMagicBytes.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4, 2), ChunkRecordCodec.ChunkPayloadVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6, 2), (ushort)_sections.Count);
        BinaryPrimitives.WriteDoubleLittleEndian(header.Slice(8, 8), _lastSimTime);

        for (int i = 0; i < _sections.Count; i++)
        {
            SectionEntry entry = _sections[i];
            int descOffset = ChunkRecordCodec.ChunkPayloadHeaderSize + i * ChunkRecordCodec.ChunkSectionDescSize;
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

        int totalSize = ChunkRecordCodec.ChunkPayloadHeaderSize +
                        _sections.Count * ChunkRecordCodec.ChunkSectionDescSize +
                        _writer.WrittenCount;
        var writer = new ArrayBufferWriter<byte>(totalSize);
        WriteTo(writer);
        return writer.WrittenSpan.ToArray();
    }

    private void CommitOpenSection(ushort sectionId, ushort flags)
    {
        int length = _writer.WrittenCount - _openSectionStart;
        _sections.Add(new SectionEntry(sectionId, flags, _openSectionStart, length));
        _sectionOpen = false;
    }

    private void AbortOpenSection()
    {
        _writer.Rewind(_openSectionStart);
        _sectionOpen = false;
    }

    private void EnsureUniqueSectionId(ushort sectionId)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            if (_sections[i].SectionId == sectionId)
                throw new ArgumentException($"Duplicate section ID {sectionId}.", nameof(sectionId));
        }
    }

    /// <summary>
    /// Stack-bound section write scope that rolls back uncommitted bytes when disposed.
    /// </summary>
    public ref struct SectionScope
    {
        private readonly ChunkPayloadBuilder? _builder;
        private readonly ushort _sectionId;
        private readonly ushort _flags;
        private bool _completed;

        internal SectionScope(ChunkPayloadBuilder builder, ushort sectionId, ushort flags)
        {
            _builder = builder;
            _sectionId = sectionId;
            _flags = flags;
            _completed = false;
        }

        /// <summary>
        /// Gets the buffer writer for writing section bytes while the scope is active.
        /// </summary>
        public IBufferWriter<byte> Writer =>
            _builder is null || _completed
                ? throw new InvalidOperationException("Section scope is no longer active.")
                : _builder._writer;

        /// <summary>
        /// Commits the written bytes as a section in the parent <see cref="ChunkPayloadBuilder"/>.
        /// </summary>
        public void Commit()
        {
            if (_builder is null || _completed)
                throw new InvalidOperationException("Section scope has already been committed or disposed.");

            _completed = true;
            _builder.CommitOpenSection(_sectionId, _flags);
        }

        /// <summary>
        /// Rewinds any uncommitted writes if <see cref="Commit"/> was not called.
        /// </summary>
        public void Dispose()
        {
            if (_builder is null || _completed)
                return;

            _completed = true;
            _builder.AbortOpenSection();
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
