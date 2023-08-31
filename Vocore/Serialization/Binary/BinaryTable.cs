using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinaryTable : BinaryValue, IEnumerable
    {
        private readonly Dictionary<string, BinaryValue> _map = new Dictionary<string, BinaryValue>();

        public BinaryTable()
            : base(BinaryValue.ValueType.Table)
        {
        }

        //
        // Properties
        //
        public ICollection<string> Keys
        {
            get { return _map.Keys; }
        }

        public ICollection<BinaryValue> Values
        {
            get { return _map.Values; }
        }
        public int Count { get { return _map.Count; } }

        //
        // Indexer
        //
        public BinaryValue this[string key]
        {
            get
            {
                if (_map.TryGetValue(key, out BinaryValue v))
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

        public bool TryGetString(string key, out string value)
        {
            if (_map.TryGetValue(key, out BinaryValue v))
            {
                return v.TryGetString(out value);
            }
            value = null;
            return false;
        }

        public bool TryGetValue<T>(string key, out T value) where T : unmanaged
        {
            if (_map.TryGetValue(key, out BinaryValue v))
            {
                return v.TryGetValue(out value);
            }
            value = default;
            return false;
        }

        public bool TryGetTable(string key, out BinaryTable value)
        {
            if (_map.TryGetValue(key, out BinaryValue v))
            {
                if (v.Type == ValueType.Table && v is BinaryTable table)
                {
                    value = table;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public bool TryGetArray(string key, out BinaryArray value)
        {
            if (_map.TryGetValue(key, out BinaryValue v))
            {
                if (v.Type == ValueType.Array && v is BinaryArray array)
                {
                    value = array;
                    return true;
                }
            }
            value = null;
            return false;
        }


        //
        // Methods
        //
        public void Clear()
        {
            _map.Clear();
        }
        public void Add(string key, BinaryValue value)
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

        public bool TryGetValue(string key, out BinaryValue value)
        {
            return _map.TryGetValue(key, out value);
        }


        IEnumerator IEnumerable.GetEnumerator()
        {
            return _map.GetEnumerator();
        }
    }
}