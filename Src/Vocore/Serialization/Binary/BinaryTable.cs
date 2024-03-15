using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinaryTable : BaseBinaryValue, IEnumerable
    {
        private readonly Dictionary<string, BaseBinaryValue> _map = new Dictionary<string, BaseBinaryValue>();

        //
        // Properties
        //
        public ICollection<string> Keys
        {
            get { return _map.Keys; }
        }

        public ICollection<BaseBinaryValue> Values
        {
            get { return _map.Values; }
        }

        public ICollection<KeyValuePair<string, BaseBinaryValue>> Entries
        {
            get { return _map; }
        }

        public int Count { get { return _map.Count; } }

        public override BinaryValueType Type => BinaryValueType.Table;

        //
        // Indexer
        //
        public BaseBinaryValue? this[string key]
        {
            get
            {
                if (_map.TryGetValue(key, out BaseBinaryValue? v))
                {
                    return v;
                }
                return null;
            }
            set
            {
                if (value == null)
                {
                    _map[key] = new BinaryValue();
                    return;
                }
                _map[key] = value;
            }
        }

        public bool TryGetString(string key, [NotNullWhen(true)] out string? value)
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetString(out value);
            }
            value = null;
            return false;
        }

        public bool TryGetValue<T>(string key, [NotNullWhen(true)] out T value) where T : unmanaged
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetValue(out value);
            }
            value = default;
            return false;
        }

        public bool TryGetNullableValue<T>(string key, [NotNullWhen(true)] out T? value) where T : unmanaged
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetNullableValue(out value);
            }
            value = default;
            return false;
        }

        public bool TryGetTable(string key, [NotNullWhen(true)] out BinaryTable? value)
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryTable binaryTable)
            {
                value = binaryTable;
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetArray(string key, [NotNullWhen(true)] out BinaryArray? value)
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryArray binaryArray)
            {

                value = binaryArray;
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetBinary(string key, [NotNullWhen(true)] out byte[]? value)
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                value = binaryValue.Bytes;
                return true;
            }
            value = null;
            return false;
        }

        public bool TryGetValue(string key, [NotNullWhen(true)] out BaseBinaryValue? value)
        {
            return _map.TryGetValue(key, out value);
        }


        //
        // Methods
        //
        public void Clear()
        {
            _map.Clear();
        }
        public void Add(string key, BaseBinaryValue value)
        {
            _map.Add(key, value);
        }


        public bool Contains(BinaryValue v)
        {
            return _map.ContainsValue(v);
        }
        public bool ContainsKey(string key)
        {
            return _map.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return _map.Remove(key);
        }

        


        IEnumerator IEnumerable.GetEnumerator()
        {
            return _map.GetEnumerator();
        }
    }
}