using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Alco
{
    /// <summary>
    /// A binary value table that maps string keys to nested binary values.
    /// </summary>
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

        /// <summary>
        /// Gets the number of entries in the table.
        /// </summary>
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

        /// <summary>
        /// Tries to read a string value by key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The string value when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the key exists and holds a string.</returns>
        public bool TryGetString(string key, [NotNullWhen(true)] out string? value)
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetString(out value);
            }
            value = null;
            return false;
        }

        /// <summary>
        /// Returns the string value for a key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <returns>The stored string.</returns>
        public string GetString(string key)
        {
            if (TryGetString(key, out string? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
        }

        /// <summary>
        /// Tries to read an unmanaged value by key.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The decoded value when found; otherwise <c>default</c>.</param>
        /// <returns><c>true</c> if the key exists and holds a compatible value.</returns>
        public bool TryGetValue<T>(string key, [NotNullWhen(true)] out T value) where T : unmanaged
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetValue(out value);
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Returns the unmanaged value for a key.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="key">The entry key.</param>
        /// <returns>The decoded value.</returns>
        public T GetValue<T>(string key) where T : unmanaged
        {
            if (TryGetValue(key, out T value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
        }

        /// <summary>
        /// Tries to read a nullable unmanaged value by key.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The decoded value when present; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the key exists and holds a readable value.</returns>
        public bool TryGetNullableValue<T>(string key, [NotNullWhen(true)] out T? value) where T : unmanaged
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetNullableValue(out value);
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Returns the nullable unmanaged value for a key.
        /// </summary>
        /// <typeparam name="T">The unmanaged value type.</typeparam>
        /// <param name="key">The entry key.</param>
        /// <returns>The decoded value, or <c>null</c> when the entry has no value.</returns>
        public T? GetNullableValue<T>(string key) where T : unmanaged
        {
            if (TryGetNullableValue(key, out T? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
        }

        /// <summary>
        /// Tries to read a nested table by key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The nested table when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the key exists and holds a table.</returns>
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

        /// <summary>
        /// Returns the nested table for a key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <returns>The stored table.</returns>
        public BinaryTable GetTable(string key)
        {
            if (TryGetTable(key, out BinaryTable? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
        }

        /// <summary>
        /// Tries to read a nested array by key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The nested array when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the key exists and holds an array.</returns>
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

        /// <summary>
        /// Returns the nested array for a key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <returns>The stored array.</returns>
        public BinaryArray GetArray(string key)
        {
            if (TryGetArray(key, out BinaryArray? value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
        }

        /// <summary>
        /// Tries to read binary data by key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The binary data when found; otherwise empty.</param>
        /// <returns><c>true</c> if the key exists and holds binary data.</returns>
        public bool TryGetBinary(string key, [NotNullWhen(true)] out ReadOnlyMemory<byte> value)
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                value = binaryValue.Bytes;
                return true;
            }
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        /// <summary>
        /// Returns the binary data for a key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <returns>The stored binary data.</returns>
        public ReadOnlyMemory<byte> GetBinary(string key)
        {
            if (TryGetBinary(key, out ReadOnlyMemory<byte> value))
            {
                return value;
            }
            throw new InvalidOperationException($"Key '{key}' not found in table.");
        }


        /// <summary>
        /// Tries to read an enum value by key.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The decoded enum value when found; otherwise <c>default</c>.</param>
        /// <returns><c>true</c> if the key exists and holds a compatible value.</returns>
        public bool TryGetEnum<T>(string key, [NotNullWhen(true)] out T value) where T : unmanaged, Enum
        {
            if (_map.TryGetValue(key, out BaseBinaryValue? v) && v is BinaryValue binaryValue)
            {
                return binaryValue.TryGetEnum(out value);
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Returns the enum value for a key.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="key">The entry key.</param>
        /// <returns>The decoded enum value.</returns>
        public T GetEnum<T>(string key) where T : unmanaged, Enum
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
        /// <summary>
        /// Removes all entries from the table.
        /// </summary>
        public void Clear()
        {
            _map.Clear();
        }
        /// <summary>
        /// Adds a nested binary value under a key.
        /// </summary>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The value to add.</param>
        public void Add(string key, BaseBinaryValue value)
        {
            _map.Add(key, value);
        }

        /// <summary>
        /// Adds an enum value under a key.
        /// </summary>
        /// <typeparam name="T">The enum type.</typeparam>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The enum value to add.</param>
        public void Add<T>(string key, T value) where T : unmanaged, Enum
        {
            _map.Add(key, BinaryValue.CreateByEnum(value));
        }


        /// <summary>
        /// Determines whether the table contains a value.
        /// </summary>
        /// <param name="v">The value to locate.</param>
        /// <returns><c>true</c> if the value is present.</returns>
        public bool Contains(BinaryValue v)
        {
            return _map.ContainsValue(v);
        }
        /// <summary>
        /// Determines whether the table contains a key.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <returns><c>true</c> if the key is present.</returns>
        public bool ContainsKey(string key)
        {
            return _map.ContainsKey(key);
        }

        /// <summary>
        /// Removes the entry with the given key.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns><c>true</c> if the entry was removed.</returns>
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