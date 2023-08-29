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
            get { return _map[key]; }
            set { _map[key] = value; }
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