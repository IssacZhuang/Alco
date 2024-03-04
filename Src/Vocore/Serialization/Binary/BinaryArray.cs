using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinaryArray : BaseBinaryValue, IEnumerable
    {

        private readonly List<BaseBinaryValue> _content = new List<BaseBinaryValue>();

        public BinaryArray()
            : base()
        {
        }

        //
        // Indexer
        //
        public BaseBinaryValue this[int index]
        {
            get { return _content[index]; }
            set { _content[index] = value; }
        }
        public int Count { get { return _content.Count; } }

        public override BinaryValueType Type => BinaryValueType.Array;

        //
        // Methods
        //
        public void Add(BaseBinaryValue v)
        {
            _content.Add(v);
        }

        public bool TryGetString(int index, [NotNullWhen(true)] out string? value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryValue binaryValue)
                {
                    return binaryValue.TryGetString(out value);
                }
            }
            value = null;
            return false;
        }

        public bool TryGetValue<T>(int index, [NotNullWhen(true)] out T value) where T : unmanaged
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryValue binaryValue)
                {
                    return binaryValue.TryGetValue(out value);
                }
            }
            value = default;
            return false;
        }

        public bool TryGetTable(int index, [NotNullWhen(true)] out BinaryTable? value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryTable binaryTable)
                {
                    value = binaryTable;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public bool TryGetArray(int index, [NotNullWhen(true)] out BinaryArray? value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryArray binaryArray)
                {
                    value = binaryArray;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public bool TryGetBinary(int index, [NotNullWhen(true)] out byte[]? value)
        {
            if (index < _content.Count)
            {
                if (_content[index] is BinaryValue binaryValue)
                {
                    value = binaryValue.Bytes;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public bool TryGetValue(int index, [NotNullWhen(true)] out BaseBinaryValue? value)
        {
            if (index < _content.Count)
            {
                value = _content[index];
                return true;
            }
            value = null;
            return false;
        }

        public int IndexOf(BaseBinaryValue item)
        {
            return _content.IndexOf(item);
        }
        public void Insert(int index, BaseBinaryValue item)
        {
            _content.Insert(index, item);
        }
        public bool Remove(BaseBinaryValue v)
        {
            return _content.Remove(v);
        }
        public void RemoveAt(int index)
        {
            _content.RemoveAt(index);
        }
        public void Clear()
        {
            _content.Clear();
        }

        public bool Contains(BaseBinaryValue v)
        {
            return _content.Contains(v);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _content.GetEnumerator();
        }
    }
}