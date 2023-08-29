using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinArray : BinaryValue, IEnumerable
    {

        private readonly List<BinaryValue> _content = new List<BinaryValue>();

        public BinArray()
            : base(BinaryValue.ValueType.Array)
        {
        }

        //
        // Indexer
        //
        public override BinaryValue this[int index]
        {
            get { return _content[index]; }
            set { _content[index] = value; }
        }
        public int Count { get { return _content.Count; } }

        //
        // Methods
        //
        public void Add(BinaryValue v)
        {
            _content.Add(v);
        }

        public int IndexOf(BinaryValue item)
        {
            return _content.IndexOf(item);
        }
        public void Insert(int index, BinaryValue item)
        {
            _content.Insert(index, item);
        }
        public bool Remove(BinaryValue v)
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

        public bool Contains(BinaryValue v)
        {
            return _content.Contains(v);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _content.GetEnumerator();
        }
    }
}