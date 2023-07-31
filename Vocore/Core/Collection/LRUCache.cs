using System;
using System.Collections.Generic;

namespace Vocore
{

    public class LRUCache<K, V>
    {
        private struct KeyValuePair
        {
            public K key;
            public V value;
            public int weight;
            public KeyValuePair(K key, V value, int weight)
            {
                this.key = key;
                this.value = value;
                this.weight = weight;
            }
        }
        private readonly Dictionary<K, LinkedListNode<KeyValuePair>> _index = new Dictionary<K, LinkedListNode<KeyValuePair>>();

        private readonly LinkedList<KeyValuePair> _leastRecentList = new LinkedList<KeyValuePair>();

        private readonly int _capacity;
        private int _sumWeight = 0;

        public LRUCache(int capacity)
        {
            this._capacity = capacity;
        }

        public bool TryGetValue(K key, out V result)
        {
            if (_index.TryGetValue(key, out var value))
            {
                result = value.Value.value;
                WasUsed(value);
                return true;
            }
            result = default(V);
            return false;
        }

        public void Add(K key, V value)
        {
            int weight = GetWeight(value);
            if (_sumWeight + weight > _capacity)
            {
                RemoveLeastUsed();
            }
            LinkedListNode<KeyValuePair> linkedListNode = new LinkedListNode<KeyValuePair>(new KeyValuePair(key, value, weight));
            _index.Add(key, linkedListNode);
            _leastRecentList.AddLast(linkedListNode);
            _sumWeight += weight;
        }

        public void Clear()
        {
            _index.Clear();
            _leastRecentList.Clear();
        }

        protected virtual int GetWeight(V value)
        {
            return 1;
        }

        private void WasUsed(LinkedListNode<KeyValuePair> node)
        {
            _leastRecentList.Remove(node);
            _leastRecentList.AddLast(node);
        }

        private void RemoveLeastUsed()
        {
            LinkedListNode<KeyValuePair> first = _leastRecentList.First;
            if (first != null)
            {
                _leastRecentList.RemoveFirst();
                _index.Remove(first.Value.key);
                _sumWeight -= first.Value.weight;
            }
        }
    }
}