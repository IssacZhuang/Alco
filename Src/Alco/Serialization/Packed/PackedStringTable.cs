using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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
        private readonly Dictionary<string, int> _keys = new(StringComparer.Ordinal);

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
            var cursor = new PackedCursor(data);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (!cursor.TryReadLengthPrefixed(out ReadOnlyMemory<byte> utf8))
                        return false;
                    Utf8.GetCharCount(utf8.Span);
                }
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            if (cursor.Remaining != 0)
                return false;

            var entries = new Entry[count];
            cursor = new PackedCursor(data);
            for (int i = 0; i < count; i++)
            {
                cursor.TryReadLengthPrefixed(out ReadOnlyMemory<byte> utf8);
                entries[i] = new Entry { Offset = cursor.Position - utf8.Length, Length = utf8.Length };
            }
            reader = new Reader(data, entries);
            return true;
        }

        public string GetString(int id)
        {
            ref Entry entry = ref _entries[id];
            return entry.Text ??= Utf8.GetString(_data.Span.Slice(entry.Offset, entry.Length));
        }

        public bool RegisterKey(int id)
        {
            string key = GetString(id);
            if (_keys.TryGetValue(key, out int oldId))
                return id == oldId;

            _keys.Add(key, id);
            return true;
        }

        public bool TryGetKeyId(string key, out int id) => _keys.TryGetValue(key, out id);
    }
}
