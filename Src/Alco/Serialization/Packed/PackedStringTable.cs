using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;

namespace Alco;

internal static class PackedStringTable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public sealed class Writer
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        private readonly List<(string Text, int ByteLength)> _entries = new();

        public int Count => _entries.Count;

        public int GetOrAdd(string? text)
        {
            text ??= string.Empty;
            if (_ids.TryGetValue(text, out int id))
                return id;

            int byteLength = Utf8.GetByteCount(text);
            id = _entries.Count;
            _ids.Add(text, id);
            _entries.Add((text, byteLength));
            return id;
        }

        public void WriteTo(IBufferWriter<byte> output)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                (string text, int length) = _entries[i];
                PackedWire.WriteVarUInt(output, length);
                if (length == 0)
                    continue;

                Utf8.GetBytes(text, output.GetSpan(length));
                output.Advance(length);
            }
        }
    }

    public sealed class Reader
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly Entry[] _entries;
        private readonly ConcurrentDictionary<string, int> _keys = new(StringComparer.Ordinal);

        private struct Entry
        {
            public int Offset;
            public int Length;
            public string? Text;
        }

        private Reader(ReadOnlyMemory<byte> data, Entry[] entries)
        {
            _data = data;
            _entries = entries;
        }

        public int Count => _entries.Length;

        public static bool TryOpen(ReadOnlyMemory<byte> data, int count, [NotNullWhen(true)] out Reader? reader)
        {
            reader = null;
            if (count < 0 || count > data.Length)
                return false;

            var entries = count == 0 ? [] : new Entry[count];
            var cursor = new PackedCursor(data);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (!cursor.TryReadLengthPrefixed(out ReadOnlyMemory<byte> utf8))
                        return false;

                    Utf8.GetCharCount(utf8.Span);
                    entries[i] = new Entry { Offset = cursor.Position - utf8.Length, Length = utf8.Length };
                }
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            if (cursor.Remaining != 0)
                return false;

            reader = new Reader(data, entries);
            return true;
        }

        public string GetString(int id)
        {
            ref Entry entry = ref _entries[id];
            string? cached = Volatile.Read(ref entry.Text);
            if (cached is not null)
                return cached;

            string decoded = Utf8.GetString(_data.Span.Slice(entry.Offset, entry.Length));
            Volatile.Write(ref entry.Text, decoded);
            return decoded;
        }

        public bool RegisterKey(int id)
        {
            string key = GetString(id);
            return _keys.GetOrAdd(key, id) == id;
        }

        public bool TryGetKeyId(string key, out int id) => _keys.TryGetValue(key, out id);
    }
}
