using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;



namespace Alco;


public class LRUCache<Tkey, TValue> where Tkey : notnull where TValue : class
{
    private struct KeyValuePair
    {
        public Tkey key;
        public TValue value;
        public int weight;
        public KeyValuePair(Tkey key, TValue value, int weight)
        {
            this.key = key;
            this.value = value;
            this.weight = weight;
        }
    }
    private readonly Dictionary<Tkey, LinkedListNode<KeyValuePair>> _index = new Dictionary<Tkey, LinkedListNode<KeyValuePair>>();

    private readonly LinkedList<KeyValuePair> _leastRecentList = new LinkedList<KeyValuePair>();

    private readonly int _capacity;
    private int _sumWeight = 0;

    public int Count => _index.Count;
    public int Capacity => _capacity;
    public int SumWeight => _sumWeight;

    public LRUCache(int capacity)
    {
        _capacity = capacity;
    }

    public bool TryGetValue(Tkey key, [NotNullWhen(true)] out TValue? result)
    {
        if (_index.TryGetValue(key, out var value))
        {
            result = value.Value.value;
            WasUsed(value);
            return true;
        }
        result = default;
        return false;
    }

    public void Add(Tkey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

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

    public bool Remove(Tkey key)
    {
        if (_index.TryGetValue(key, out var node))
        {
            _leastRecentList.Remove(node);
            _index.Remove(key);
            _sumWeight -= node.Value.weight;
            return true;
        }
        return false;
    }

    protected virtual int GetWeight(TValue value)
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
        LinkedListNode<KeyValuePair>? first = _leastRecentList.First;
        if (first != null)
        {
            _leastRecentList.RemoveFirst();
            _index.Remove(first.Value.key);
            _sumWeight -= first.Value.weight;
        }
    }
}
