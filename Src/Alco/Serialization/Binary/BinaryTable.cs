using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Alco
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

        public string GetString(string key)
        {
            if (TryGetString(key, out string? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
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

        public T GetValue<T>(string key) where T : unmanaged
        {
            if (TryGetValue(key, out T value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
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

        public T? GetNullableValue<T>(string key) where T : unmanaged
        {
            if (TryGetNullableValue(key, out T? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
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

        public BinaryTable GetTable(string key)
        {
            if (TryGetTable(key, out BinaryTable? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
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

        public BinaryArray GetArray(string key)
        {
            if (TryGetArray(key, out BinaryArray? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
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

        public byte[] GetBinary(string key)
        {
            if (TryGetBinary(key, out byte[]? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
        }


        public bool TryGetEnum<T>(string key, [NotNullWhen(true)] out T value) where T : struct, Enum
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetEnum(out value);
            }
            value = default;
            return false;
        }

        public T GetEnum<T>(string key) where T : struct, Enum
        {
            if (TryGetEnum(key, out T value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
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

        public void Add<T>(string key, T value) where T : struct, Enum
        {
            _map.Add(key, BinaryValue.CreateValueEnum(value));
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