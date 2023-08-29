using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinObject : BinValue, IEnumerable
    {
        private Dictionary<string, BinValue> mMap = new Dictionary<string, BinValue>();

        public BinObject()
            : base(BinValue.ValueType.Object)
        {
        }

        //
        // Properties
        //
        public ICollection<string> Keys
        {
            get { return mMap.Keys; }
        }

        public ICollection<BinValue> Values
        {
            get { return mMap.Values; }
        }
        public int Count { get { return mMap.Count; } }

        //
        // Indexer
        //
        public override BinValue this[string key]
        {
            get { return mMap[key]; }
            set { mMap[key] = value; }
        }
        //
        // Methods
        //
        public override void Clear()
        {
            mMap.Clear();
        }
        public override void Add(string key, BinValue value)
        {
            mMap.Add(key, value);
        }


        public override bool Contains(BinValue v)
        {
            return mMap.ContainsValue(v);
        }
        public override bool ContainsKey(string key)
        {
            return mMap.ContainsKey(key);
        }

        public bool Remove(string key)
        {
            return mMap.Remove(key);
        }

        public bool TryGetValue(string key, out BinValue value)
        {
            return mMap.TryGetValue(key, out value);
        }


        IEnumerator IEnumerable.GetEnumerator()
        {
            return mMap.GetEnumerator();
        }
    }
}