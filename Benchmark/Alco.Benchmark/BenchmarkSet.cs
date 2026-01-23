using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using System.Linq;

namespace Alco.Benchmark;

[MemoryDiagnoser]
public class BenchmarkSet
{
    private const int Count = 1000;
    private int[] _data;
    private HashSet<int> _hashSet;
    private SortedSet<int> _sortedSet;

    [GlobalSetup]
    public void Setup()
    {
        _data = Enumerable.Range(0, Count).ToArray();
        // Shuffle data to make it more realistic
        var rnd = new System.Random(42);
        for (int i = 0; i < _data.Length; i++)
        {
            int randomIndex = rnd.Next(i, _data.Length);
            (_data[i], _data[randomIndex]) = (_data[randomIndex], _data[i]);
        }

        _hashSet = new HashSet<int>(_data);
        _sortedSet = new SortedSet<int>(_data);
    }

    [Benchmark(Description = "HashSet Add")]
    public void HashSetAdd()
    {
        var set = new HashSet<int>();
        for (int i = 0; i < Count; i++)
        {
            set.Add(_data[i]);
        }
    }

    [Benchmark(Description = "SortedSet Add")]
    public void SortedSetAdd()
    {
        var set = new SortedSet<int>();
        for (int i = 0; i < Count; i++)
        {
            set.Add(_data[i]);
        }
    }

    [Benchmark(Description = "HashSet Contains")]
    public bool HashSetContains()
    {
        bool result = false;
        for (int i = 0; i < Count; i++)
        {
            result ^= _hashSet.Contains(i);
        }
        return result;
    }

    [Benchmark(Description = "SortedSet Contains")]
    public bool SortedSetContains()
    {
        bool result = false;
        for (int i = 0; i < Count; i++)
        {
            result ^= _sortedSet.Contains(i);
        }
        return result;
    }

    [Benchmark(Description = "HashSet Remove")]
    public void HashSetRemove()
    {
        var set = new HashSet<int>(_data);
        for (int i = 0; i < Count; i++)
        {
            set.Remove(_data[i]);
        }
    }

    [Benchmark(Description = "SortedSet Remove")]
    public void SortedSetRemove()
    {
        var set = new SortedSet<int>(_data);
        for (int i = 0; i < Count; i++)
        {
            set.Remove(_data[i]);
        }
    }
}
