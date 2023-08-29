using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vocore
{
    public class BinArray : BinValue, IEnumerable
    {

        private readonly List<BinValue> _content = new List<BinValue>();

        public BinArray()
            : base(BinValue.ValueType.Array)
        {
        }

        //
        // Indexer
        //
        public override BinValue this[int index]
        {
            get { return _content[index]; }
            set { _content[index] = value; }
        }
        public int Count { get { return _content.Count; } }

        //
        // Methods
        //
        public override void Add(BinValue v)
        {
            _content.Add(v);
        }

        public int IndexOf(BinValue item)
        {
            return _content.IndexOf(item);
        }
        public void Insert(int index, BinValue item)
        {
            _content.Insert(index, item);
        }
        public bool Remove(BinValue v)
        {
            return _content.Remove(v);
        }
        public void RemoveAt(int index)
        {
            _content.RemoveAt(index);
        }
        public override void Clear()
        {
            _content.Clear();
        }

        public override bool Contains(BinValue v)
        {
            return _content.Contains(v);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _content.GetEnumerator();
        }
    }
}